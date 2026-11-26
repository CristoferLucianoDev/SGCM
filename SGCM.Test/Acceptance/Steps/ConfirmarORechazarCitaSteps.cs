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
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class ConfirmarORechazarCitaSteps(ApiScenario scenario) : IDisposable
{
    private const string DoctorAppointmentsUrl = "/api/doctors/me/appointments";
    private const string ConfirmAction = "confirm";
    private const string RejectAction = "reject";
    private const string Pending = "pending";
    private const string Confirmed = "confirmed";
    private const string Rejected = "rejected";
    private const string Cancelled = "cancelled";
    private const string Completed = "completed";
    private const string RejectionReason = "Agenda completa";
    private const string TimeFormat = "HH:mm";
    private const int AppointmentHours = 2;
    private const int BlockMarginHours = 1;
    private const int SantoDomingoOffsetHours = -4;
    private const int ClientErrorMinimum = 400;
    private const int ClientErrorMaximum = 499;

    private sealed record PatientSeed(int PatientId, string Email);

    // Los estados del esquema se escriben en español; la base los guarda en inglés.
    private static readonly Dictionary<string, string> StatusBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pendiente"] = Pending,
        ["confirmada"] = Confirmed,
        ["rechazada"] = Rejected,
        ["cancelada"] = Cancelled,
        ["completada"] = Completed
    };

    private string _doctorEmail = string.Empty;
    private int _doctorId;
    private int _specialtyId;
    private PatientSeed? _patient;
    private DateTimeOffset _startsAt;
    private int _appointmentId;
    private JsonDocument? _problem;

    [Given("que inicié sesión como médico para decidir solicitudes de cita")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        var userId = 0;
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "decidir-medico", id => userId = id);
        // Se lee con SQL porque el correo es un objeto de valor que EF convierte desde la columna.
        _doctorEmail = await WithContextAsync(context => context.Database.SqlQuery<string>(
            $"SELECT email AS \"Value\" FROM app.\"user\" WHERE id = {userId}").SingleAsync());
        _doctorId = await WithContextAsync(context => context.Doctors.Where(doctor => doctor.UserId == userId)
            .Select(doctor => doctor.Id).SingleAsync());
        _specialtyId = await WithContextAsync(async context =>
        {
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, _doctorId, specialtyId);
            return specialtyId;
        });
    }

    [Given("que en mi agenda hay una cita {string} de mi paciente para dentro de {int} día(s) a las {string}")]
    public async Task DadoQueHayUnaCitaDentroDeDias(string status, int days, string time)
    {
        var day = LocalDate(scenario.Factory.Clock.UtcNow).AddDays(days);
        await SeedAppointmentAsync(_doctorId, _specialtyId, status, Local(day, ParseTime(time)).ToUniversalTime());
    }

    [Given("que en mi agenda hay una cita {string} de mi paciente que empieza dentro de {int} hora(s)")]
    public async Task DadoQueHayUnaCitaDentroDeHoras(string status, int hours)
    {
        // La hora en punto actual más N horas siempre queda a menos de N horas del instante actual.
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        await SeedAppointmentAsync(_doctorId, _specialtyId, status, currentHour.AddHours(hours));
    }

    [Given("que otro médico tiene en su agenda una cita {string} para dentro de {int} día(s) a las {string}")]
    public async Task DadoQueOtroMedicoTieneUnaCita(string status, int days, string time)
    {
        var (otherDoctorId, otherSpecialtyId) = await SeedOtherDoctorAsync();
        var day = LocalDate(scenario.Factory.Clock.UtcNow).AddDays(days);
        await SeedAppointmentAsync(otherDoctorId, otherSpecialtyId, status, Local(day, ParseTime(time)).ToUniversalTime());
    }

    [When("decido confirmar la solicitud")]
    public Task CuandoDecidoConfirmar() => ConfirmAsync();

    [When("decido confirmar esa cita ajena")]
    public Task CuandoDecidoConfirmarLaCitaAjena() => ConfirmAsync();

    [When("decido rechazar la solicitud indicando el motivo {string}")]
    public Task CuandoDecidoRechazarConMotivo(string reason) => RejectAsync(reason);

    [When("decido rechazar la solicitud sin indicar un motivo")]
    public Task CuandoDecidoRechazarSinMotivo() => RejectAsync(null);

    [Then("la solicitud de mi agenda queda {string}")]
    public async Task EntoncesLaSolicitudQueda(string spanishStatus)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        await AssertStatusAsync(spanishStatus);
    }

    [Then("la solicitud de mi agenda sigue {string}")]
    public Task EntoncesLaSolicitudSigue(string spanishStatus) => AssertStatusAsync(spanishStatus);

    [Then("la cita de ese médico sigue {string}")]
    public Task EntoncesLaCitaDeEseMedicoSigue(string spanishStatus) => AssertStatusAsync(spanishStatus);

    [Then("el horario de esa solicitud queda libre")]
    public async Task EntoncesElHorarioQuedaLibre()
    {
        var activeCount = await WithContextAsync(context => context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM scheduling.appointment
            WHERE doctor_id = {_doctorId} AND starts_at = {_startsAt} AND status IN ('pending', 'confirmed')
            """).SingleAsync());

        activeCount.ShouldBe(0);
    }

    [Then("el paciente recibe un correo de confirmación con los datos de la cita a las {string} y yo no recibo correo")]
    public void EntoncesElPacienteRecibeLaConfirmacion(string startTime)
    {
        // Un único correo en todo el escenario: si el médico recibiera uno, habría dos.
        var notification = scenario.Factory.Notifications.Sent.ShouldHaveSingleItem()
            .ShouldBeOfType<AppointmentConfirmedNotification>();

        notification.Recipient.Email.ShouldBe(_patient!.Email);
        notification.Appointment.AppointmentId.ShouldBe(_appointmentId);
        notification.Appointment.StartTime.ShouldBe(ParseTime(startTime));
        notification.Appointment.EndTime.ShouldBe(ParseTime(startTime).AddHours(AppointmentHours));
        notification.Appointment.DoctorEmail.ShouldBe(_doctorEmail);
    }

    [Then("el paciente recibe un correo de rechazo con el motivo {string} y yo no recibo correo")]
    public void EntoncesElPacienteRecibeElRechazo(string reason)
    {
        var notification = scenario.Factory.Notifications.Sent.ShouldHaveSingleItem()
            .ShouldBeOfType<AppointmentRejectedNotification>();

        notification.Recipient.Email.ShouldBe(_patient!.Email);
        notification.RejectionReason.ShouldBe(reason);
        notification.Appointment.AppointmentId.ShouldBe(_appointmentId);
    }

    [Then("no se envía ningún correo por mi decisión")]
    public void EntoncesNoSeEnviaNingunCorreo() => scenario.Factory.Notifications.Sent.ShouldBeEmpty();

    [Then("mi decisión sobre la solicitud se rechaza con el código {string}")]
    public async Task EntoncesMiDecisionSeRechaza(string code)
    {
        _problem ??= JsonDocument.Parse(await scenario.Response!.Content.ReadAsStringAsync());

        ((int)scenario.Response!.StatusCode).ShouldBeInRange(ClientErrorMinimum, ClientErrorMaximum);
        _problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    /// <summary>Libera el documento del último ProblemDetails; la respuesta y el cliente los libera el escenario.</summary>
    public void Dispose() => _problem?.Dispose();

    private Task ConfirmAsync() => SendDecisionAsync(ConfirmAction, content: null);

    private Task RejectAsync(string? reason) => SendDecisionAsync(RejectAction, JsonContent.Create(new { reason }));

    private async Task SendDecisionAsync(string action, HttpContent? content)
    {
        scenario.Response?.Dispose();
        _problem?.Dispose();
        _problem = null;
        scenario.Response = await scenario.Client.PostAsync($"{DoctorAppointmentsUrl}/{_appointmentId}/{action}", content);
    }

    private async Task AssertStatusAsync(string spanishStatus)
    {
        var status = await WithContextAsync(context => context.Database.SqlQuery<string>(
            $"SELECT status AS \"Value\" FROM scheduling.appointment WHERE id = {_appointmentId}").SingleAsync());

        status.ShouldBe(StatusBySpanishName[spanishStatus]);
    }

    /// <summary>
    /// Paciente nuevo con su bloque abierto y una cita con el médico indicado en el estado pedido. La cita se
    /// inserta pendiente o confirmada (trigger T2) y pasa por transiciones válidas hasta el estado final.
    /// </summary>
    private async Task SeedAppointmentAsync(int doctorId, int specialtyId, string spanishStatus, DateTimeOffset startsAtUtc)
    {
        var status = StatusBySpanishName[spanishStatus];
        _patient = await SeedPatientAsync();
        _startsAt = startsAtUtc;
        var blockStart = startsAtUtc.AddHours(-BlockMarginHours);
        var blockEnd = startsAtUtc.AddHours(AppointmentHours + BlockMarginHours);
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
        var email = $"decidir-paciente-{Guid.NewGuid():N}@correo.com";
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
        var email = $"decidir-medico-ajeno-{Guid.NewGuid():N}@correo.com";
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

    private static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, TimeFormat, CultureInfo.InvariantCulture);
}
