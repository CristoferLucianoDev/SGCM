using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class CancelarCitaSteps(ApiScenario scenario)
{
    private const string AppointmentsUrl = "/api/appointments";
    private const string Pending = "pending";
    private const string Confirmed = "confirmed";
    private const string Rejected = "rejected";
    private const string Cancelled = "cancelled";
    private const string Completed = "completed";
    private const string RejectionReason = "Agenda completa";
    private const int AppointmentHours = 2;
    private const int BlockMarginHours = 1;
    private const int SantoDomingoOffsetHours = -4;

    private sealed record DoctorSeed(int DoctorId, int SpecialtyId, string Email);

    private sealed record AppointmentState(string Status, string? StatusReason);

    // Los estados del esquema se escriben en español; la base los guarda en inglés.
    private static readonly Dictionary<string, string> StatusBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pendiente"] = Pending,
        ["confirmada"] = Confirmed,
        ["rechazada"] = Rejected,
        ["cancelada"] = Cancelled,
        ["completada"] = Completed
    };

    private int _patientUserId;
    private DoctorSeed? _doctor;
    private DateTimeOffset _startsAt;
    private int _appointmentId;
    private JsonElement? _problem;

    [Given("que inicié sesión como paciente para cancelar una cita")]
    public Task DadoQueInicieSesionComoPaciente() =>
        AuthenticatedScenario.LoginAsync(scenario, "Patient", "cancelar-paciente", id => _patientUserId = id);

    [Given("que tengo una cita {string} con un médico dentro de {int} días a las {string}")]
    public async Task DadoQueTengoUnaCitaDentroDeDias(string status, int days, string time)
    {
        var day = LocalDate(scenario.Factory.Clock.UtcNow).AddDays(days);
        await SeedAppointmentAsync(await PatientIdAsync(_patientUserId), status, Local(day, Time(time)).ToUniversalTime());
    }

    [Given("que tengo una cita {string} con un médico que empieza dentro de {int} horas")]
    public async Task DadoQueTengoUnaCitaDentroDeHoras(string status, int hours)
    {
        // La hora en punto actual más N horas (N menor que el plazo) siempre queda dentro del plazo vedado.
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        await SeedAppointmentAsync(await PatientIdAsync(_patientUserId), status, currentHour.AddHours(hours));
    }

    [Given("que otro paciente tiene una cita {string} con un médico dentro de {int} días a las {string}")]
    public async Task DadoQueOtroPacienteTieneUnaCita(string status, int days, string time)
    {
        var email = $"cancelar-ajeno-{Guid.NewGuid():N}@correo.com";
        var other = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(other);
        await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, other.Id);
            return 0;
        });
        var day = LocalDate(scenario.Factory.Clock.UtcNow).AddDays(days);
        await SeedAppointmentAsync(await PatientIdAsync(other.Id), status, Local(day, Time(time)).ToUniversalTime());
    }

    [When("cancelo mi cita indicando el motivo {string}")]
    public Task CuandoCanceloMiCitaConMotivo(string reason) => CancelAsync(new { reason });

    [When("cancelo mi cita sin indicar un motivo")]
    public Task CuandoCanceloMiCitaSinMotivo() => CancelAsync(new { reason = (string?)null });

    [When("cancelo esa cita ajena indicando el motivo {string}")]
    public Task CuandoCanceloLaCitaAjena(string reason) => CancelAsync(new { reason });

    [Then("la cita queda cancelada con el motivo {string}")]
    public async Task EntoncesLaCitaQuedaCanceladaConMotivo(string reason)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        (await ReadStateAsync()).ShouldBe(new AppointmentState(Cancelled, reason));
    }

    [Then("la cita queda cancelada sin motivo")]
    public async Task EntoncesLaCitaQuedaCanceladaSinMotivo()
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        (await ReadStateAsync()).ShouldBe(new AppointmentState(Cancelled, null));
    }

    [Then("el médico ya no tiene citas activas en ese horario")]
    public async Task EntoncesElMedicoNoTieneCitasActivas()
    {
        var count = await WithContextAsync(context => context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM scheduling.appointment
            WHERE doctor_id = {_doctor!.DoctorId} AND starts_at = {_startsAt} AND status IN ('pending', 'confirmed')
            """).SingleAsync());

        count.ShouldBe(0);
    }

    [Then("el médico recibe el aviso de la cancelación con el motivo {string} y el paciente no recibe correo")]
    public void EntoncesElMedicoRecibeElAvisoConMotivo(string reason) =>
        SingleCancellationNotification().CancellationReason.ShouldBe(reason);

    [Then("el médico recibe el aviso de la cancelación sin motivo")]
    public void EntoncesElMedicoRecibeElAvisoSinMotivo() =>
        SingleCancellationNotification().CancellationReason.ShouldBeNull();

    [Then("la cancelación se rechaza con el código {string}")]
    public async Task EntoncesLaCancelacionSeRechaza(string code)
    {
        var problem = await ProblemAsync();

        ((int)scenario.Response!.StatusCode).ShouldBeInRange(400, 499);
        problem.GetProperty("code").GetString().ShouldBe(code);
    }

    [Then("el mensaje de la cancelación indica que debo llamar al médico")]
    public async Task EntoncesElMensajeIndicaLlamarAlMedico()
    {
        var problem = await ProblemAsync();

        problem.GetProperty("title").GetString().ShouldNotBeNull().ShouldContain("llamar al médico");
    }

    [Then("mi cita sigue {string}")]
    public Task EntoncesMiCitaSigue(string status) => AssertStatusUnchangedAsync(status);

    [Then("la cita ajena sigue {string}")]
    public Task EntoncesLaCitaAjenaSigue(string status) => AssertStatusUnchangedAsync(status);

    private async Task CancelAsync(object body)
    {
        scenario.Response?.Dispose();
        _problem = null;
        scenario.Response = await scenario.Client.PostAsJsonAsync($"{AppointmentsUrl}/{_appointmentId}/cancel", body);
    }

    private AppointmentCancelledNotification SingleCancellationNotification()
    {
        var notification = scenario.Factory.Notifications.Sent.ShouldHaveSingleItem()
            .ShouldBeOfType<AppointmentCancelledNotification>();

        notification.Recipient.Email.ShouldBe(_doctor!.Email);
        notification.CancelledBy.ShouldBe(AppointmentCanceller.Patient);
        return notification;
    }

    private async Task AssertStatusUnchangedAsync(string spanishStatus) =>
        (await ReadStateAsync()).Status.ShouldBe(StatusBySpanishName[spanishStatus]);

    /// <summary>ProblemDetails de la última solicitud; el cuerpo de la respuesta solo se puede leer una vez.</summary>
    private async Task<JsonElement> ProblemAsync() =>
        _problem ??= await scenario.Response!.Content.ReadFromJsonAsync<JsonElement>();

    private Task<AppointmentState> ReadStateAsync() =>
        WithContextAsync(context => context.Database.SqlQuery<AppointmentState>(
            $"SELECT status, status_reason FROM scheduling.appointment WHERE id = {_appointmentId}").SingleAsync());

    private Task<int> PatientIdAsync(int userId) =>
        WithContextAsync(context => context.Patients.Where(patient => patient.UserId == userId)
            .Select(patient => patient.Id).SingleAsync());

    /// <summary>
    /// Médico con su bloque abierto y una cita del paciente en el estado pedido. La cita se inserta pendiente o
    /// confirmada (trigger T2) y pasa por transiciones válidas hasta el estado final.
    /// </summary>
    private async Task SeedAppointmentAsync(int patientId, string spanishStatus, DateTimeOffset startsAtUtc)
    {
        var status = StatusBySpanishName[spanishStatus];
        var doctor = await EnsureDoctorAsync();
        _startsAt = startsAtUtc;
        var endsAtUtc = startsAtUtc.AddHours(AppointmentHours);
        var blockStart = startsAtUtc.AddHours(-BlockMarginHours);
        var blockEnd = endsAtUtc.AddHours(BlockMarginHours);
        var finalReason = status == Rejected ? RejectionReason : null;
        _appointmentId = await WithContextAsync(async context =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({doctor.DoctorId}, NULL, {blockStart}, {blockEnd}, true)
                """);
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status, finalReason);
        });
    }

    // Un solo médico por escenario: con la especialidad nueva y el perfil completo.
    private async Task<DoctorSeed> EnsureDoctorAsync()
    {
        if (_doctor is not null) return _doctor;

        var email = $"cancelar-medico-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        _doctor = await WithContextAsync(async context =>
        {
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            return new DoctorSeed(doctorId, specialtyId, email);
        });

        return _doctor;
    }

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // Santo Domingo está en UTC-4 todo el año.
    private static DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);

    private static DateTimeOffset Local(DateOnly date, TimeOnly time) =>
        new(date.ToDateTime(time), TimeSpan.FromHours(SantoDomingoOffsetHours));

    private static TimeOnly Time(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
}
