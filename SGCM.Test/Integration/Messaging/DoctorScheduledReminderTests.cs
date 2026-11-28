using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Messaging.Notifications;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Test.Integration.Scheduling;
using SGCM.Web.Contracts.Messaging;
using SGCM.Web.Jobs;
using Shouldly;

namespace SGCM.Test.Integration.Messaging;

/// <summary>
/// Recordatorios que programa el médico, contra la API real y la base: la vista previa con los datos reales de la
/// cita, el texto final guardado aunque la plantilla cambie, el envío a su hora con el resultado registrado, y los
/// rechazos por fecha pasada, cita ajena o cita no confirmada. El reloj es el FakeDateTimeProvider de la fábrica.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DoctorScheduledReminderTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string Url = "/api/doctors/me/appointments";

    private sealed record ReminderRow(
        int Id,
        int? TemplateId,
        string MessageBody,
        DateTimeOffset ScheduledAt,
        DateTimeOffset? SentAt,
        string DeliveryStatus,
        int? CreatedBy);

    [Fact]
    public async Task La_vista_previa_completa_la_plantilla_con_los_datos_reales_de_la_cita()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());
        var templateId = await GlobalTemplateIdAsync();

        using var response = await doctor.Client.GetAsync($"{Url}/{appointmentId}/reminders/preview?templateId={templateId}");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var specialty = await SpecialtyNameAsync(doctor.SpecialtyId);
        (await response.Content.ReadFromJsonAsync<ReminderPreviewResponse>()).ShouldBe(new ReminderPreviewResponse(
            templateId,
            $"Recordatorio: su cita de {specialty} es manana",
            $"Hola Ana Pérez, le recordamos su cita de {specialty} con Luis Pérez el {Date(Today().AddDays(DaysAhead))} a las " +
            $"{AppointmentStartHour:00}:00. Si no puede asistir, cancele o reprograme la cita desde la aplicacion."));
    }

    [Fact]
    public async Task El_texto_editado_se_guarda_y_llegada_la_hora_el_paciente_lo_recibe_y_queda_registrado_el_envio()
    {
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());
        var templateId = await GlobalTemplateIdAsync();
        var preview = await PreviewAsync(doctor, appointmentId, templateId);
        var edited = $"{preview.Message} Traiga sus estudios.";
        var sendAt = CurrentHourUtc().AddDays(2);

        var scheduled = await ScheduleAsync(doctor, appointmentId, new { templateId, message = edited, scheduledAt = sendAt });

        scheduled.AppointmentId.ShouldBe(appointmentId);
        scheduled.TemplateId.ShouldBe(templateId);
        scheduled.Message.ShouldBe(edited);
        scheduled.ScheduledAt.ShouldBe(sendAt);
        var row = (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem();
        row.ShouldBe(new ReminderRow(scheduled.Id, templateId, edited, sendAt, null, "pending", (await FindUserAsync(doctor.Email))!.Id));

        Factory.Notifications.Clear();
        Clock.UtcNow = sendAt.AddSeconds(30);
        await RunJobAsync();

        var sent = Factory.Notifications.Sent.OfType<ReminderNotification>().ShouldHaveSingleItem();
        sent.Recipient.Email.ShouldBe(patient.Email);
        sent.Message.ShouldBe(edited);
        sent.Subject.ShouldBe(preview.Subject);
        row = (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem();
        row.DeliveryStatus.ShouldBe("sent");
        row.SentAt.ShouldBe(sendAt.AddSeconds(30));
    }

    [Fact]
    public async Task Cambiar_la_plantilla_despues_no_altera_el_texto_guardado()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());
        var templateId = await InsertPrivateTemplateAsync(doctor.DoctorId, "Hola {PatientName}, traiga sus estudios.");
        var preview = await PreviewAsync(doctor, appointmentId, templateId);
        var sendAt = CurrentHourUtc().AddDays(1);
        await ScheduleAsync(doctor, appointmentId, new { templateId, message = preview.Message, scheduledAt = sendAt });

        const string newBody = "Hola {PatientName}, texto nuevo.";
        await WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE messaging.reminder_template SET body_template = {newBody} WHERE id = {templateId}"));
        Factory.Notifications.Clear();
        Clock.UtcNow = sendAt.AddSeconds(30);
        await RunJobAsync();

        preview.Message.ShouldBe("Hola Ana Pérez, traiga sus estudios.");
        (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem().MessageBody.ShouldBe(preview.Message);
        Factory.Notifications.Sent.OfType<ReminderNotification>().ShouldHaveSingleItem().Message.ShouldBe(preview.Message);
    }

    [Fact]
    public async Task Un_mensaje_libre_en_una_cita_completada_se_programa_sin_plantilla_y_llega_con_el_asunto_por_defecto()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync("completed", InTimeStart());
        var sendAt = CurrentHourUtc().AddHours(3);

        var scheduled = await ScheduleAsync(
            doctor, appointmentId, new { message = "¿Cómo sigue después de su consulta?", scheduledAt = sendAt });

        scheduled.TemplateId.ShouldBeNull();
        Factory.Notifications.Clear();
        Clock.UtcNow = sendAt.AddSeconds(30);
        await RunJobAsync();
        var sent = Factory.Notifications.Sent.OfType<ReminderNotification>().ShouldHaveSingleItem();
        sent.Message.ShouldBe("¿Cómo sigue después de su consulta?");
        sent.Subject.ShouldStartWith("Recordatorio de su cita de ");
    }

    [Fact]
    public async Task Si_el_correo_falla_el_recordatorio_queda_registrado_como_fallido()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());
        var sendAt = CurrentHourUtc().AddDays(1);
        await ScheduleAsync(doctor, appointmentId, new { message = "Hola Ana", scheduledAt = sendAt });
        Factory.Notifications.FailWhen = notification => notification is ReminderNotification;

        Clock.UtcNow = sendAt.AddSeconds(30);
        await RunJobAsync();

        var row = (await ReadRemindersAsync(appointmentId)).ShouldHaveSingleItem();
        row.DeliveryStatus.ShouldBe("failed");
        row.SentAt.ShouldBeNull();
    }

    [Fact]
    public async Task Una_fecha_pasada_se_rechaza_sin_guardar()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());

        using var response = await doctor.Client.PostAsJsonAsync(
            $"{Url}/{appointmentId}/reminders", new { message = "Hola Ana", scheduledAt = Clock.UtcNow.AddMinutes(-1) });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Reminder.ScheduledInPast");
        (await ReadRemindersAsync(appointmentId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task La_cita_de_otro_medico_responde_como_inexistente_en_la_vista_previa_y_al_programar()
    {
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());
        var other = await SeedDoctorAsync();
        var templateId = await GlobalTemplateIdAsync();

        using (var preview = await other.Client.GetAsync($"{Url}/{appointmentId}/reminders/preview?templateId={templateId}"))
        {
            await AssertErrorAsync(preview, HttpStatusCode.NotFound, "Appointment.NotFound");
        }

        using var schedule = await other.Client.PostAsJsonAsync(
            $"{Url}/{appointmentId}/reminders", new { message = "Hola Ana", scheduledAt = CurrentHourUtc().AddDays(1) });
        await AssertErrorAsync(schedule, HttpStatusCode.NotFound, "Appointment.NotFound");
        (await ReadRemindersAsync(appointmentId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task La_plantilla_privada_de_otro_medico_responde_como_inexistente()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());
        var other = await SeedDoctorAsync();
        var templateId = await InsertPrivateTemplateAsync(other.DoctorId, "Hola {PatientName}.");

        using var response = await doctor.Client.GetAsync($"{Url}/{appointmentId}/reminders/preview?templateId={templateId}");

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "ReminderTemplate.NotFound");
    }

    [Fact]
    public async Task Una_cita_pendiente_no_admite_recordatorios()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());

        using var response = await doctor.Client.PostAsJsonAsync(
            $"{Url}/{appointmentId}/reminders", new { message = "Hola Ana", scheduledAt = CurrentHourUtc().AddDays(1) });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Reminder.AppointmentNotEligible");
    }

    [Fact]
    public async Task Un_paciente_no_puede_programar_recordatorios()
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());

        using var response = await patient.Client.PostAsJsonAsync(
            $"{Url}/{appointmentId}/reminders", new { message = "Hola", scheduledAt = CurrentHourUtc().AddDays(1) });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    private static async Task<ReminderPreviewResponse> PreviewAsync(DoctorSeed doctor, int appointmentId, int templateId)
    {
        using var response = await doctor.Client.GetAsync($"{Url}/{appointmentId}/reminders/preview?templateId={templateId}");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ReminderPreviewResponse>())!;
    }

    private static async Task<ScheduledReminderResponse> ScheduleAsync(DoctorSeed doctor, int appointmentId, object body)
    {
        using var response = await doctor.Client.PostAsJsonAsync($"{Url}/{appointmentId}/reminders", body);
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ScheduledReminderResponse>())!;
    }

    private Task RunJobAsync() => Factory.GetJob<SendDueRemindersJob>().RunOnceAsync(CancellationToken.None);

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

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

    // reset.sql borra las plantillas privadas, así que cada prueba siembra las suyas.
    private async Task<int> InsertPrivateTemplateAsync(int doctorId, string body)
    {
        var id = 0;
        await WithContextAsync(async context => id = (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO messaging.reminder_template (doctor_id, name, subject, body_template)
            VALUES ({doctorId}, 'Indicaciones', 'Indicaciones para su cita', {body})
            RETURNING id AS "Value"
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
