using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Application.Messaging.Notifications;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Jobs;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Pasos del recordatorio automático. El médico inicia sesión, la cita es de un paciente nuevo y cada horario lleva
/// su propio bloque. El reloj de la aplicación es el FakeDateTimeProvider de la fábrica.
/// </summary>
[Binding]
public sealed class RecordatorioAutomaticoSteps(ApiScenario scenario)
{
    private const string Url = "/api/doctors/me/appointments";
    private const int AppointmentHours = 2;
    private const int LeadHours = 24;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);

    private sealed record ReminderRow(int Id, int? TemplateId, string MessageBody, DateTimeOffset ScheduledAt, string DeliveryStatus);

    private int _doctorUserId;
    private int _doctorId;
    private int _appointmentId;
    private string _patientEmail = string.Empty;
    private DateTimeOffset _startsAt;
    private DateTimeOffset _newStartsAt;

    [Given("que inicié sesión como médico con una cita pendiente dentro de {int} días a las {string}")]
    public async Task DadoQueTengoUnaCitaPendiente(int days, string time)
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "recordatorio-medico", userId => _doctorUserId = userId);
        _startsAt = Local(Today().AddDays(days), time);
        _patientEmail = $"recordatorio-paciente-{Guid.NewGuid():N}@correo.com";
        var patient = UserMother.WithRole(UserRole.Patient, email: Email.Create(_patientEmail).Value);
        await scenario.Factory.SaveAsync(patient);
        (_doctorId, _appointmentId) = await WithContextAsync(async context =>
        {
            var doctorId = await context.Database.SqlQuery<int>(
                $"SELECT id AS \"Value\" FROM people.doctor WHERE user_id = {_doctorUserId}").SingleAsync();
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            await TestDataSeeder.InsertPatientProfileAsync(context, patient.Id);
            var patientId = await context.Patients.Where(value => value.UserId == patient.Id).Select(value => value.Id).SingleAsync();
            await InsertBlockAsync(context, doctorId, _startsAt);
            var appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, doctorId, specialtyId, _startsAt.ToUniversalTime(), AppointmentHours, "pending");
            return (doctorId, appointmentId);
        });
    }

    [Given("que confirmé la cita del recordatorio")]
    [When("confirmo la cita del recordatorio")]
    public async Task CuandoConfirmoLaCita()
    {
        using var response = await scenario.Client.PostAsync($"{Url}/{_appointmentId}/confirm", content: null);
        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    [When("llega la hora del recordatorio y corre el envío de recordatorios")]
    public async Task CuandoLlegaLaHora()
    {
        scenario.Factory.Notifications.Clear();
        scenario.Factory.Clock.UtcNow = _startsAt.ToUniversalTime().AddHours(-LeadHours).AddSeconds(30);
        await scenario.Factory.GetJob<SendDueRemindersJob>().RunOnceAsync(CancellationToken.None);
    }

    [When("cancelo la cita del recordatorio con el motivo {string}")]
    public async Task CuandoCanceloLaCita(string reason)
    {
        using var response = await scenario.Client.PostAsJsonAsync($"{Url}/{_appointmentId}/cancel", new { reason });
        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    [When("reprogramo la cita del recordatorio a dentro de {int} días a las {string}")]
    public Task CuandoReprogramoEnDias(int days, string time) => RescheduleAsync(Local(Today().AddDays(days), time));

    [When("reprogramo la cita del recordatorio a dentro de {int} horas")]
    public Task CuandoReprogramoEnHoras(int hours)
    {
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        return RescheduleAsync(new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(hours));
    }

    [Then("queda programado un recordatorio 24 horas antes de la cita con la plantilla global")]
    public async Task EntoncesQuedaProgramado()
    {
        var reminder = (await RemindersAsync()).ShouldHaveSingleItem();
        reminder.DeliveryStatus.ShouldBe("pending");
        reminder.ScheduledAt.ShouldBe(_startsAt.AddHours(-LeadHours));
        reminder.TemplateId.ShouldBe(await GlobalTemplateIdAsync());
        reminder.MessageBody.ShouldContain($"el {Date(_startsAt)} a las {_startsAt:HH:mm}");
    }

    [Then("el paciente recibe el recordatorio con el texto programado")]
    public async Task EntoncesElPacienteRecibeElRecordatorio()
    {
        var sent = scenario.Factory.Notifications.Sent.OfType<ReminderNotification>().ShouldHaveSingleItem();
        sent.Recipient.Email.ShouldBe(_patientEmail);
        sent.Message.ShouldBe((await RemindersAsync()).ShouldHaveSingleItem().MessageBody);
    }

    [Then("el recordatorio queda registrado como {string}")]
    public async Task EntoncesElRecordatorioQuedaComo(string status) =>
        (await RemindersAsync()).ShouldHaveSingleItem().DeliveryStatus.ShouldBe(status);

    [Then("el recordatorio anterior queda anulado y hay uno nuevo 24 horas antes de la hora nueva")]
    public async Task EntoncesSeRecalcula()
    {
        var reminders = await RemindersAsync();
        reminders.Count.ShouldBe(2);
        reminders[0].DeliveryStatus.ShouldBe("cancelled");
        reminders[1].DeliveryStatus.ShouldBe("pending");
        reminders[1].ScheduledAt.ShouldBe(_newStartsAt.AddHours(-LeadHours));
        reminders[1].MessageBody.ShouldContain($"el {Date(_newStartsAt)} a las {_newStartsAt.ToOffset(SantoDomingo):HH:mm}");
    }

    [Then("la cita no tiene recordatorios pendientes")]
    public async Task EntoncesNoHayPendientes() =>
        (await RemindersAsync()).ShouldAllBe(reminder => reminder.DeliveryStatus != "pending");

    private async Task RescheduleAsync(DateTimeOffset startsAt)
    {
        _newStartsAt = startsAt;
        await WithContextAsync(async context =>
        {
            await InsertBlockAsync(context, _doctorId, startsAt);
            return 0;
        });
        using var response = await scenario.Client.PostAsJsonAsync(
            $"{Url}/{_appointmentId}/reschedule", new { startsAt = startsAt.ToUniversalTime() });
        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    // El bloque cubre exactamente la cita, como exige el trigger de coherencia de la cita.
    private static Task InsertBlockAsync(AppDbContext context, int doctorId, DateTimeOffset startsAt)
    {
        var startsAtUtc = startsAt.ToUniversalTime();
        return context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctorId}, NULL, {startsAtUtc}, {startsAtUtc.AddHours(AppointmentHours)}, true)
            """);
    }

    // Las columnas van en snake_case porque SqlQuery aplica la misma convención de nombres que el contexto.
    private Task<List<ReminderRow>> RemindersAsync() =>
        WithContextAsync(context => context.Database.SqlQuery<ReminderRow>(
            $"""
            SELECT id, template_id, message_body, scheduled_at, delivery_status
            FROM messaging.reminder
            WHERE appointment_id = {_appointmentId}
            ORDER BY id
            """).ToListAsync());

    private Task<int> GlobalTemplateIdAsync() =>
        WithContextAsync(context => context.Database.SqlQuery<int>(
            $"""
            SELECT id AS "Value" FROM messaging.reminder_template
            WHERE doctor_id IS NULL AND name = 'Recordatorio de cita'
            """).SingleAsync());

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private DateOnly Today() => DateOnly.FromDateTime(scenario.Factory.Clock.UtcNow.ToOffset(SantoDomingo).DateTime);

    private static string Date(DateTimeOffset instant) =>
        instant.ToOffset(SantoDomingo).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static DateTimeOffset Local(DateOnly date, string time) =>
        new(date.ToDateTime(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture)), SantoDomingo);
}
