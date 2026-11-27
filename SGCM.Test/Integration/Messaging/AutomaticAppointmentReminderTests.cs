using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Messaging.Notifications;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Test.Integration.Scheduling;
using SGCM.Web.Jobs;
using Shouldly;

namespace SGCM.Test.Integration.Messaging;

/// <summary>
/// Recordatorio automático de cita contra la API real y la base: se programa 24 horas antes al confirmarse la cita,
/// llega al paciente a su hora, se anula al cancelarla y se recalcula al reprogramarla. El reloj de la aplicación es
/// el FakeDateTimeProvider de la fábrica.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AutomaticAppointmentReminderTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string DoctorAppointmentsUrl = "/api/doctors/me/appointments";
    private const int LeadHours = 24;

    private sealed record ReminderRow(
        int Id,
        int? TemplateId,
        string MessageBody,
        DateTimeOffset ScheduledAt,
        DateTimeOffset? SentAt,
        string DeliveryStatus,
        int? CreatedBy);

    [Fact]
    public async Task Al_confirmarse_la_cita_programa_el_recordatorio_24_horas_antes_con_la_plantilla_global()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, startsAt);

        await ConfirmAsync(doctor, appointmentId);

        var reminder = (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem();
        reminder.DeliveryStatus.ShouldBe("pending");
        reminder.ScheduledAt.ShouldBe(startsAt.AddHours(-LeadHours));
        reminder.TemplateId.ShouldBe(await GlobalTemplateIdAsync());
        reminder.CreatedBy.ShouldBeNull();
        reminder.MessageBody.ShouldBe(
            $"Hola Ana Pérez, le recordamos su cita de {await SpecialtyNameAsync(doctor.SpecialtyId)} con Luis Pérez el " +
            $"{Date(Today().AddDays(DaysAhead))} a las {AppointmentStartHour:00}:00. Si no puede asistir, cancele o " +
            "reprograme la cita desde la aplicacion.");
    }

    [Fact]
    public async Task Llegada_la_hora_el_paciente_recibe_el_recordatorio_y_queda_registrado_como_enviado()
    {
        var startsAt = InTimeStart();
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, startsAt);
        await ConfirmAsync(doctor, appointmentId);
        var scheduled = (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem();
        Factory.Notifications.Clear();

        Clock.UtcNow = scheduled.ScheduledAt.AddMinutes(-1);
        await RunJobAsync();
        Factory.Notifications.Sent.ShouldBeEmpty();
        (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem().DeliveryStatus.ShouldBe("pending");

        Clock.UtcNow = scheduled.ScheduledAt.AddSeconds(30);
        await RunJobAsync();

        var sent = Factory.Notifications.Sent.OfType<ReminderNotification>().ShouldHaveSingleItem();
        sent.Recipient.Email.ShouldBe(patient.Email);
        sent.Message.ShouldBe(scheduled.MessageBody);
        sent.Subject.ShouldBe($"Recordatorio: su cita de {await SpecialtyNameAsync(doctor.SpecialtyId)} es manana");
        var row = (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem();
        row.DeliveryStatus.ShouldBe("sent");
        row.SentAt.ShouldBe(scheduled.ScheduledAt.AddSeconds(30));

        // Es idempotente: otra ejecución no lo reenvía.
        await RunJobAsync();
        Factory.Notifications.Sent.OfType<ReminderNotification>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Si_el_correo_falla_el_recordatorio_queda_registrado_como_fallido()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        await ConfirmAsync(doctor, appointmentId);
        var scheduled = (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem();
        Factory.Notifications.FailWhen = notification => notification is ReminderNotification;

        Clock.UtcNow = scheduled.ScheduledAt.AddSeconds(30);
        await RunJobAsync();

        var row = (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem();
        row.DeliveryStatus.ShouldBe("failed");
        row.SentAt.ShouldBeNull();
    }

    [Fact]
    public async Task Si_la_cita_se_cancela_el_recordatorio_se_anula_y_no_se_envia()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        await ConfirmAsync(doctor, appointmentId);
        var scheduled = (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem();

        using (var cancel = await doctor.Client.PostAsJsonAsync(
            $"{DoctorAppointmentsUrl}/{appointmentId}/cancel", new { reason = "Viaje al congreso" }))
        {
            await cancel.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem().DeliveryStatus.ShouldBe("cancelled");
        Factory.Notifications.Clear();
        Clock.UtcNow = scheduled.ScheduledAt.AddSeconds(30);
        await RunJobAsync();
        Factory.Notifications.Sent.OfType<ReminderNotification>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Si_el_medico_la_reprograma_el_recordatorio_se_recalcula_con_la_hora_y_el_texto_nuevos()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        await ConfirmAsync(doctor, appointmentId);
        var newStartsAt = Local(Today().AddDays(DaysAhead + 2), AppointmentStartHour + 3).ToUniversalTime();
        await InsertBlockAsync(doctor.DoctorId, newStartsAt, newStartsAt.AddHours(AppointmentHours));

        await RescheduleAsync(doctor, appointmentId, newStartsAt);

        var reminders = await ReadRemindersAsync(appointmentId);
        reminders.Count.ShouldBe(2);
        reminders[0].DeliveryStatus.ShouldBe("cancelled");
        var recalculated = reminders[1];
        recalculated.DeliveryStatus.ShouldBe("pending");
        recalculated.ScheduledAt.ShouldBe(newStartsAt.AddHours(-LeadHours));
        recalculated.MessageBody.ShouldContain($"el {Date(Today().AddDays(DaysAhead + 2))} a las {AppointmentStartHour + 3:00}:00");
    }

    [Fact]
    public async Task Si_la_cita_queda_a_menos_de_24_horas_no_se_programa_el_recordatorio()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        await ConfirmAsync(doctor, appointmentId);
        var soon = CurrentHourUtc().AddHours(5);
        await InsertBlockAsync(doctor.DoctorId, soon, soon.AddHours(AppointmentHours));

        await RescheduleAsync(doctor, appointmentId, soon);

        (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem().DeliveryStatus.ShouldBe("cancelled");
    }

    private static async Task ConfirmAsync(DoctorSeed doctor, int appointmentId)
    {
        using var response = await doctor.Client.PostAsync($"{DoctorAppointmentsUrl}/{appointmentId}/confirm", content: null);
        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    private static async Task RescheduleAsync(DoctorSeed doctor, int appointmentId, DateTimeOffset startsAt)
    {
        using var response = await doctor.Client.PostAsJsonAsync(
            $"{DoctorAppointmentsUrl}/{appointmentId}/reschedule", new { startsAt });
        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private Task RunJobAsync() => Factory.GetJob<SendDueRemindersJob>().RunOnceAsync(CancellationToken.None);

    // Las columnas van en snake_case porque SqlQuery aplica la misma convención de nombres que el contexto.
    private async Task<List<ReminderRow>> ReadRemindersAsync(int appointmentId)
    {
        var rows = new List<ReminderRow>();
        await WithContextAsync(async context => rows = await context.Database.SqlQuery<ReminderRow>(
            $"""
            SELECT id, template_id, message_body, scheduled_at, sent_at, delivery_status, created_by
            FROM messaging.reminder
            WHERE appointment_id = {appointmentId}
            ORDER BY id
            """).ToListAsync());
        return rows;
    }

    private async Task<int> GlobalTemplateIdAsync()
    {
        var id = 0;
        await WithContextAsync(async context => id = (await context.Database.SqlQuery<int>(
            $"""
            SELECT id AS "Value" FROM messaging.reminder_template
            WHERE doctor_id IS NULL AND name = 'Recordatorio de cita'
            """).ToListAsync()).Single());
        return id;
    }

    private async Task<string> SpecialtyNameAsync(int specialtyId)
    {
        var name = string.Empty;
        await WithContextAsync(async context => name = await context.Specialties
            .Where(specialty => specialty.Id == specialtyId).Select(specialty => specialty.Name).SingleAsync());
        return name;
    }
}
