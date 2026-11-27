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
/// Pasos de la reprogramación de una cita por el médico. El médico inicia sesión con la hora real y el reloj de la app
/// no se mueve: paciente, bloques y citas se siembran con <see cref="TestDataSeeder"/> relativos a la hora actual.
/// </summary>
[Binding]
public sealed class ReprogramarCitaPorElMedicoSteps(ApiScenario scenario) : IDisposable
{
    private const string DoctorAppointmentsUrl = "/api/doctors/me/appointments";
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

    private int _doctorId;
    private int _specialtyId;
    private string _patientEmail = string.Empty;
    private int _appointmentId;
    private DateTimeOffset _originalStartsAt;
    private JsonDocument? _problem;

    [Given("que inicié sesión como médico para reprogramar mis citas")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        var userId = 0;
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "reprogramar-medico", id => userId = id);
        _doctorId = await WithContextAsync(context => context.Doctors.Where(doctor => doctor.UserId == userId)
            .Select(doctor => doctor.Id).SingleAsync());
        _specialtyId = await WithContextAsync(async context =>
        {
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, _doctorId, specialtyId);
            return specialtyId;
        });
    }

    [Given("que tengo en mi agenda una cita {string} de un paciente para dentro de {int} día(s) a las {string}")]
    public Task DadoQueTengoUnaCitaDentroDeDias(string spanishStatus, int days, string time) =>
        SeedAppointmentAsync(spanishStatus, SlotAt(days, time));

    [Given("que tengo en mi agenda una cita {string} de un paciente que empieza dentro de {int} hora(s)")]
    public Task DadoQueTengoUnaCitaDentroDeHoras(string spanishStatus, int hours) =>
        // La hora en punto actual más N horas siempre queda a menos de N horas del instante actual.
        SeedAppointmentAsync(spanishStatus, CurrentHour().AddHours(hours));

    [Given("que tengo un horario libre en mis bloques dentro de {int} día(s) a las {string}")]
    public Task DadoQueTengoUnHorarioLibreDentroDeDias(int days, string time) =>
        InsertBlockAroundAsync(SlotAt(days, time));

    [Given("que tengo un horario libre en mis bloques que empieza dentro de {int} hora(s)")]
    public Task DadoQueTengoUnHorarioLibreDentroDeHoras(int hours) =>
        InsertBlockAroundAsync(CurrentHour().AddHours(hours));

    [Given("que otro paciente ya ocupa mi horario dentro de {int} día(s) a las {string}")]
    public async Task DadoQueOtroPacienteOcupaMiHorario(int days, string time)
    {
        var slot = SlotAt(days, time);
        await InsertBlockAroundAsync(slot);
        var (otherPatientId, _) = await SeedPatientAsync();
        await InsertAppointmentAsync(otherPatientId, Confirmed, slot);
    }

    [When("muevo esa cita a dentro de {int} día(s) a las {string}")]
    public Task CuandoMuevoEsaCitaDentroDeDias(int days, string time) => SendAsync(SlotAt(days, time));

    [When("muevo esa cita a la hora que empieza dentro de {int} hora(s)")]
    public Task CuandoMuevoEsaCitaDentroDeHoras(int hours) => SendAsync(CurrentHour().AddHours(hours));

    [When("muevo esa cita a la hora que empezó hace {int} hora(s)")]
    public Task CuandoMuevoEsaCitaAUnaHoraPasada(int hours) => SendAsync(CurrentHour().AddHours(-hours));

    [Then("el cambio de horario de la cita se acepta")]
    public Task EntoncesElCambioSeAcepta() => scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

    [Then("la cita movida queda marcada como {string}")]
    public async Task EntoncesLaCitaMovidaQuedaMarcada(string spanishStatus)
    {
        var status = await WithContextAsync(context => context.Database.SqlQuery<string>(
            $"SELECT status AS \"Value\" FROM scheduling.appointment WHERE id = {_appointmentId}").SingleAsync());

        status.ShouldBe(StatusBySpanishName[spanishStatus]);
    }

    [Then("la cita movida empieza dentro de {int} día(s) a las {string}")]
    public async Task EntoncesLaCitaMovidaEmpieza(int days, string time)
    {
        // La misma fila, con el mismo id, tiene el nuevo inicio y el fin según la duración configurada.
        var expectedStart = SlotAt(days, time);
        var rows = await WithContextAsync(context => context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM scheduling.appointment
            WHERE id = {_appointmentId}
              AND starts_at = {expectedStart}
              AND ends_at = {expectedStart.AddHours(AppointmentHours)}
            """).SingleAsync());

        rows.ShouldBe(1);
    }

    [Then("el paciente recibe un solo correo con la nueva fecha de dentro de {int} día(s) a las {string}")]
    public void EntoncesElPacienteRecibeElCorreo(int days, string time)
    {
        var notification = scenario.Factory.Notifications.Sent.ShouldHaveSingleItem()
            .ShouldBeOfType<AppointmentRescheduledNotification>();

        notification.Recipient.Email.ShouldBe(_patientEmail);
        notification.TemplateKey.ShouldBe(AppointmentRescheduledNotification.RescheduledByDoctorTemplateKey);
        notification.Appointment.AppointmentId.ShouldBe(_appointmentId);
        notification.Appointment.AppointmentDate.ShouldBe(LocalDate(SlotAt(days, time)));
        notification.Appointment.StartTime.ShouldBe(ParseTime(time));
    }

    [Then("el cambio de horario de la cita se rechaza con el código {string}")]
    public async Task EntoncesElCambioSeRechaza(string code)
    {
        _problem ??= JsonDocument.Parse(await scenario.Response!.Content.ReadAsStringAsync());

        ((int)scenario.Response!.StatusCode).ShouldBeInRange(ClientErrorMinimum, ClientErrorMaximum);
        _problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    [Then("la cita que quise mover sigue en su horario original")]
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

    private async Task SendAsync(DateTimeOffset startsAt)
    {
        scenario.Response?.Dispose();
        _problem?.Dispose();
        _problem = null;
        scenario.Response = await scenario.Client.PostAsJsonAsync(
            $"{DoctorAppointmentsUrl}/{_appointmentId}/{RescheduleAction}", new { startsAt });
    }

    /// <summary>Paciente nuevo con su bloque abierto y una cita con el médico de la sesión, en el estado pedido.</summary>
    private async Task SeedAppointmentAsync(string spanishStatus, DateTimeOffset startsAtUtc)
    {
        _originalStartsAt = startsAtUtc;
        await InsertBlockAroundAsync(startsAtUtc);
        var (patientId, email) = await SeedPatientAsync();
        _patientEmail = email;
        _appointmentId = await InsertAppointmentAsync(patientId, StatusBySpanishName[spanishStatus], startsAtUtc);
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

    private async Task<(int PatientId, string Email)> SeedPatientAsync()
    {
        var email = $"reprogramar-medico-paciente-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        var patientId = await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
            return await context.Patients.Where(patient => patient.UserId == user.Id)
                .Select(patient => patient.Id).SingleAsync();
        });

        return (patientId, email);
    }

    /// <summary>Hora en punto actual en UTC según el reloj de la app, truncada.</summary>
    private DateTimeOffset CurrentHour()
    {
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
    }

    // Santo Domingo está en UTC-4 todo el año.
    private DateTimeOffset SlotAt(int days, string time)
    {
        var today = DateOnly.FromDateTime(
            scenario.Factory.Clock.UtcNow.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);

        return new DateTimeOffset(
            today.AddDays(days).ToDateTime(ParseTime(time)), TimeSpan.FromHours(SantoDomingoOffsetHours)).ToUniversalTime();
    }

    private static DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);

    private static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, TimeFormat, CultureInfo.InvariantCulture);

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
