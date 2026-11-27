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

/// <summary>
/// Pasos de la reprogramación de una cita por el paciente. El paciente inicia sesión con la hora real y el reloj de la
/// app no se mueve: médico, bloques y citas se siembran con <see cref="TestDataSeeder"/> relativos a la hora actual.
/// </summary>
[Binding]
public sealed class ReprogramarCitaSteps(ApiScenario scenario) : IDisposable
{
    private const string PatientAppointmentsUrl = "/api/patients/me/appointments";
    private const string RescheduleAction = "reschedule";
    private const string Pending = "pending";
    private const string Confirmed = "confirmed";
    private const string TimeFormat = "HH:mm";
    private const int AppointmentHours = 2;
    private const int BlockMarginHours = 1;
    private const int SantoDomingoOffsetHours = -4;
    private const int ClientErrorMinimum = 400;
    private const int ClientErrorMaximum = 499;

    // Los estados del escenario se escriben en español; la base los guarda en inglés.
    private static readonly Dictionary<string, string> StatusBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pendiente"] = Pending,
        ["confirmada"] = Confirmed
    };

    private int _patientId;
    private int _doctorId;
    private int _specialtyId;
    private string _doctorEmail = string.Empty;
    private int _appointmentId;
    private DateTimeOffset _originalStartsAt;
    private JsonDocument? _problem;

    [Given("que inicié sesión como paciente para reprogramar mis citas")]
    public async Task DadoQueInicieSesionComoPaciente()
    {
        var userId = 0;
        await AuthenticatedScenario.LoginAsync(scenario, "Patient", "reprogramar-paciente", id => userId = id);
        _patientId = await WithContextAsync(context => context.Patients.Where(patient => patient.UserId == userId)
            .Select(patient => patient.Id).SingleAsync());
    }

    [Given("que tengo una cita {string} para reprogramar con mi médico dentro de {int} día(s) a las {string}")]
    public async Task DadoQueTengoUnaCitaDentroDeDias(string spanishStatus, int days, string time)
    {
        await SeedDoctorAsync();
        await SeedOwnAppointmentAsync(spanishStatus, SlotAt(days, time));
    }

    [Given("que tengo una cita {string} para reprogramar con mi médico que empieza dentro de {int} hora(s)")]
    public async Task DadoQueTengoUnaCitaDentroDeHoras(string spanishStatus, int hours)
    {
        await SeedDoctorAsync();
        // La hora en punto actual más N horas siempre queda a menos de N horas del instante actual.
        await SeedOwnAppointmentAsync(spanishStatus, CurrentHour().AddHours(hours));
    }

    [Given("que el médico de esa cita tiene un horario libre dentro de {int} día(s) a las {string}")]
    public Task DadoQueElMedicoTieneUnHorarioLibre(int days, string time) =>
        InsertBlockAroundAsync(SlotAt(days, time));

    [Given("que otro paciente ya ocupa el horario del médico de esa cita dentro de {int} día(s) a las {string}")]
    public async Task DadoQueOtroPacienteOcupaElHorario(int days, string time)
    {
        var slot = SlotAt(days, time);
        await InsertBlockAroundAsync(slot);
        var otherPatientId = await SeedPatientAsync();
        await InsertAppointmentAsync(otherPatientId, Confirmed, slot);
    }

    [When("reprogramo esa cita para dentro de {int} día(s) a las {string}")]
    public async Task CuandoReprogramoEsaCita(int days, string time)
    {
        scenario.Response?.Dispose();
        _problem?.Dispose();
        _problem = null;
        // El cuerpo lleva la hora con el desfase de Santo Domingo, como la escribiría el cliente.
        var startsAt = LocalSlotAt(days, time);
        scenario.Response = await scenario.Client.PostAsJsonAsync(
            $"{PatientAppointmentsUrl}/{_appointmentId}/{RescheduleAction}", new { startsAt });
    }

    [Then("la reprogramación se acepta")]
    public Task EntoncesLaReprogramacionSeAcepta() => scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

    [Then("la cita conserva su identidad y queda en el horario de dentro de {int} día(s) a las {string}")]
    public async Task EntoncesLaCitaConservaSuIdentidad(int days, string time)
    {
        // La misma fila, con el mismo id, tiene ahora el nuevo inicio y el fin según la duración configurada.
        var expectedStart = SlotAt(days, time);
        var rows = await WithContextAsync(context => context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM scheduling.appointment
            WHERE id = {_appointmentId}
              AND patient_id = {_patientId}
              AND starts_at = {expectedStart}
              AND ends_at = {expectedStart.AddHours(AppointmentHours)}
            """).SingleAsync());

        rows.ShouldBe(1);
    }

    [Then("la cita reprogramada vuelve a estar {string}")]
    public async Task EntoncesLaCitaVuelveAEstar(string spanishStatus)
    {
        var status = await WithContextAsync(context => context.Database.SqlQuery<string>(
            $"SELECT status AS \"Value\" FROM scheduling.appointment WHERE id = {_appointmentId}").SingleAsync());

        status.ShouldBe(StatusBySpanishName[spanishStatus]);
    }

    [Then("el médico recibe un solo aviso de reprogramación con el horario de dentro de {int} día(s) a las {string}")]
    public void EntoncesElMedicoRecibeElAviso(int days, string time)
    {
        var notification = scenario.Factory.Notifications.Sent.ShouldHaveSingleItem()
            .ShouldBeOfType<AppointmentRescheduledNotification>();

        notification.Recipient.Email.ShouldBe(_doctorEmail);
        notification.Appointment.AppointmentId.ShouldBe(_appointmentId);
        notification.Appointment.AppointmentDate.ShouldBe(LocalDate(SlotAt(days, time)));
        notification.Appointment.StartTime.ShouldBe(ParseTime(time));
    }

    [Then("la reprogramación de la cita se rechaza con el código {string}")]
    public async Task EntoncesLaReprogramacionSeRechaza(string code)
    {
        _problem ??= JsonDocument.Parse(await scenario.Response!.Content.ReadAsStringAsync());

        ((int)scenario.Response!.StatusCode).ShouldBeInRange(ClientErrorMinimum, ClientErrorMaximum);
        _problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    [Then("la cita reprogramable sigue en su horario original")]
    public async Task EntoncesLaCitaSigueEnSuHorarioOriginal()
    {
        var rows = await WithContextAsync(context => context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM scheduling.appointment
            WHERE id = {_appointmentId} AND starts_at = {_originalStartsAt} AND status = {Confirmed}
            """).SingleAsync());

        rows.ShouldBe(1);
        scenario.Factory.Notifications.Sent.ShouldBeEmpty();
    }

    /// <summary>Libera el documento del último ProblemDetails; la respuesta y el cliente los libera el escenario.</summary>
    public void Dispose() => _problem?.Dispose();

    /// <summary>Médico nuevo con perfil completo y una especialidad; queda como el médico de la cita del escenario.</summary>
    private async Task SeedDoctorAsync()
    {
        _doctorEmail = $"reprogramar-medico-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create(_doctorEmail).Value);
        await scenario.Factory.SaveAsync(user);
        (_doctorId, _specialtyId) = await WithContextAsync(async context =>
        {
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            return (doctorId, specialtyId);
        });
    }

    /// <summary>Bloque abierto que contiene la cita del paciente que inició sesión y la cita misma, en el estado pedido.</summary>
    private async Task SeedOwnAppointmentAsync(string spanishStatus, DateTimeOffset startsAtUtc)
    {
        _originalStartsAt = startsAtUtc;
        await InsertBlockAroundAsync(startsAtUtc);
        _appointmentId = await InsertAppointmentAsync(_patientId, StatusBySpanishName[spanishStatus], startsAtUtc);
    }

    private Task InsertBlockAroundAsync(DateTimeOffset startsAtUtc)
    {
        var blockStart = startsAtUtc.AddHours(-BlockMarginHours);
        var blockEnd = startsAtUtc.AddHours(AppointmentHours + BlockMarginHours);

        return WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({_doctorId}, NULL, {blockStart}, {blockEnd}, true)
            """));
    }

    private Task<int> InsertAppointmentAsync(int patientId, string status, DateTimeOffset startsAtUtc) =>
        WithContextAsync(context => TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patientId, _doctorId, _specialtyId, startsAtUtc, AppointmentHours, status));

    private async Task<int> SeedPatientAsync()
    {
        var user = UserMother.WithRole(
            UserRole.Patient, email: Email.Create($"reprogramar-otro-paciente-{Guid.NewGuid():N}@correo.com").Value);
        await scenario.Factory.SaveAsync(user);

        return await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
            return await context.Patients.Where(patient => patient.UserId == user.Id)
                .Select(patient => patient.Id).SingleAsync();
        });
    }

    /// <summary>Hora en punto actual en UTC según el reloj de la app, truncada.</summary>
    private DateTimeOffset CurrentHour()
    {
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
    }

    // Santo Domingo está en UTC-4 todo el año.
    private DateTimeOffset LocalSlotAt(int days, string time)
    {
        var today = DateOnly.FromDateTime(
            scenario.Factory.Clock.UtcNow.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);

        return new DateTimeOffset(
            today.AddDays(days).ToDateTime(ParseTime(time)), TimeSpan.FromHours(SantoDomingoOffsetHours));
    }

    private DateTimeOffset SlotAt(int days, string time) => LocalSlotAt(days, time).ToUniversalTime();

    private static DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);

    private static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, TimeFormat, CultureInfo.InvariantCulture);

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
