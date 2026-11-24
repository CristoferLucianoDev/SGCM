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
public sealed class CancelacionDeCitaPorElMedicoSteps(ApiScenario scenario)
{
    private const string DoctorAppointmentsUrl = "/api/doctors/me/appointments";
    private const string Pending = "pending";
    private const string Confirmed = "confirmed";
    private const string Rejected = "rejected";
    private const string Cancelled = "cancelled";
    private const string Completed = "completed";
    private const string RejectionReason = "Agenda completa";
    private const string Word = "palabra";
    private const int AppointmentHours = 2;
    private const int BlockMarginHours = 1;
    private const int SantoDomingoOffsetHours = -4;

    private sealed record PatientSeed(int PatientId, string Email);

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

    private int _doctorId;
    private int _specialtyId;
    private PatientSeed? _patient;
    private int _appointmentId;
    private string? _reason;
    private JsonElement? _problem;

    [Given("que inicié sesión como médico para cancelar una cita")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        var userId = 0;
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "cancelar-medico", id => userId = id);
        _doctorId = await WithContextAsync(context => context.Doctors.Where(doctor => doctor.UserId == userId)
            .Select(doctor => doctor.Id).SingleAsync());
        _specialtyId = await WithContextAsync(async context =>
        {
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, _doctorId, specialtyId);
            return specialtyId;
        });
    }

    [Given("que mi paciente tiene una cita {string} conmigo dentro de {int} días a las {string}")]
    public async Task DadoQueMiPacienteTieneUnaCitaDentroDeDias(string status, int days, string time)
    {
        var day = LocalDate(scenario.Factory.Clock.UtcNow).AddDays(days);
        await SeedAppointmentAsync(_doctorId, _specialtyId, status, Local(day, Time(time)).ToUniversalTime());
    }

    [Given("que mi paciente tiene una cita {string} conmigo que empieza dentro de {int} horas")]
    public async Task DadoQueMiPacienteTieneUnaCitaDentroDeHoras(string status, int hours)
    {
        // La hora en punto actual más N horas siempre queda a menos de N horas del instante actual.
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        await SeedAppointmentAsync(_doctorId, _specialtyId, status, currentHour.AddHours(hours));
    }

    [Given("que otro médico tiene una cita {string} con un paciente dentro de {int} días a las {string}")]
    public async Task DadoQueOtroMedicoTieneUnaCita(string status, int days, string time)
    {
        var (otherDoctorId, otherSpecialtyId) = await SeedOtherDoctorAsync();
        var day = LocalDate(scenario.Factory.Clock.UtcNow).AddDays(days);
        await SeedAppointmentAsync(otherDoctorId, otherSpecialtyId, status, Local(day, Time(time)).ToUniversalTime());
    }

    [When("como médico cancelo la cita indicando el motivo {string}")]
    public Task CuandoCanceloConMotivo(string reason) => CancelAsync(reason);

    [When("como médico cancelo la cita sin indicar un motivo")]
    public Task CuandoCanceloSinMotivo() => CancelAsync(null);

    [When("como médico cancelo la cita con un motivo de {int} palabras")]
    public Task CuandoCanceloConMotivoDePalabras(int words) =>
        CancelAsync(string.Join(" ", Enumerable.Repeat(Word, words)));

    [When("como médico cancelo esa cita de otro médico indicando el motivo {string}")]
    public Task CuandoCanceloLaCitaDeOtroMedico(string reason) => CancelAsync(reason);

    [Then("la cita queda cancelada por el médico con el motivo {string}")]
    public async Task EntoncesLaCitaQuedaCanceladaConMotivo(string reason)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        (await ReadStateAsync()).ShouldBe(new AppointmentState(Cancelled, reason));
    }

    [Then("la cita del médico queda cancelada")]
    public async Task EntoncesLaCitaDelMedicoQuedaCancelada()
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        (await ReadStateAsync()).Status.ShouldBe(Cancelled);
    }

    [Then("el paciente recibe el aviso de la cancelación con el motivo dado y yo no recibo correo")]
    public void EntoncesElPacienteRecibeElAviso()
    {
        // Un único correo en todo el escenario: si el médico recibiera uno, habría dos.
        var notification = scenario.Factory.Notifications.Sent.ShouldHaveSingleItem()
            .ShouldBeOfType<AppointmentCancelledNotification>();

        notification.Recipient.Email.ShouldBe(_patient!.Email);
        notification.CancelledBy.ShouldBe(AppointmentCanceller.Doctor);
        notification.CancellationReason.ShouldBe(_reason);
    }

    [Then("la cancelación del médico se rechaza con el código {string}")]
    public async Task EntoncesLaCancelacionDelMedicoSeRechaza(string code)
    {
        var problem = await ProblemAsync();

        ((int)scenario.Response!.StatusCode).ShouldBeInRange(400, 499);
        problem.GetProperty("code").GetString().ShouldBe(code);
    }

    [Then("la cita del médico sigue {string}")]
    public Task EntoncesLaCitaDelMedicoSigue(string status) => AssertStatusUnchangedAsync(status);

    [Then("esa cita de otro médico sigue {string}")]
    public Task EntoncesEsaCitaDeOtroMedicoSigue(string status) => AssertStatusUnchangedAsync(status);

    private async Task CancelAsync(string? reason)
    {
        _reason = reason;
        scenario.Response?.Dispose();
        _problem = null;
        scenario.Response = await scenario.Client.PostAsJsonAsync(
            $"{DoctorAppointmentsUrl}/{_appointmentId}/cancel", new { reason });
    }

    private async Task AssertStatusUnchangedAsync(string spanishStatus) =>
        (await ReadStateAsync()).Status.ShouldBe(StatusBySpanishName[spanishStatus]);

    /// <summary>ProblemDetails de la última solicitud; el cuerpo de la respuesta solo se puede leer una vez.</summary>
    private async Task<JsonElement> ProblemAsync() =>
        _problem ??= await scenario.Response!.Content.ReadFromJsonAsync<JsonElement>();

    private Task<AppointmentState> ReadStateAsync() =>
        WithContextAsync(context => context.Database.SqlQuery<AppointmentState>(
            $"SELECT status, status_reason FROM scheduling.appointment WHERE id = {_appointmentId}").SingleAsync());

    /// <summary>
    /// Paciente nuevo con su bloque abierto y una cita con el médico indicado en el estado pedido. La cita se
    /// inserta pendiente o confirmada (trigger T2) y pasa por transiciones válidas hasta el estado final.
    /// </summary>
    private async Task SeedAppointmentAsync(int doctorId, int specialtyId, string spanishStatus, DateTimeOffset startsAtUtc)
    {
        var status = StatusBySpanishName[spanishStatus];
        _patient = await SeedPatientAsync();
        var endsAtUtc = startsAtUtc.AddHours(AppointmentHours);
        var blockStart = startsAtUtc.AddHours(-BlockMarginHours);
        var blockEnd = endsAtUtc.AddHours(BlockMarginHours);
        var finalReason = status == Rejected ? RejectionReason : null;
        _appointmentId = await WithContextAsync(async context =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({doctorId}, NULL, {blockStart}, {blockEnd}, true)
                """);
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, _patient.PatientId, doctorId, specialtyId, startsAtUtc, AppointmentHours, status, finalReason);
        });
    }

    private async Task<PatientSeed> SeedPatientAsync()
    {
        var email = $"cancelar-medico-paciente-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        var patientId = await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
            return await context.Patients.Where(patient => patient.UserId == user.Id)
                .Select(patient => patient.Id).SingleAsync();
        });

        return new PatientSeed(patientId, email);
    }

    // Médico distinto del que inició sesión, con perfil completo y una especialidad nueva.
    private async Task<(int DoctorId, int SpecialtyId)> SeedOtherDoctorAsync()
    {
        var email = $"cancelar-medico-ajeno-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        return await WithContextAsync(async context =>
        {
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            return (doctorId, specialtyId);
        });
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
