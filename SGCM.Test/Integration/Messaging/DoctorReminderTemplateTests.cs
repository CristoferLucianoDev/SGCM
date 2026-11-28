using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// Plantillas propias del médico contra la API real y la base: crear, editar, desactivar y listar; la variable no
/// permitida, la global de solo lectura, la plantilla de otro médico y que editar no cambia los recordatorios ya
/// programados o enviados.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DoctorReminderTemplateTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string Url = "/api/doctors/me/reminder-templates";
    private const string Body = "Hola {PatientName}, traiga sus estudios a la cita del {AppointmentDate}.";

    private sealed record TemplateRow(int? DoctorId, string Name, string Subject, string BodyTemplate, bool IsActive);

    [Fact]
    public async Task El_medico_crea_una_plantilla_activa_propia()
    {
        var doctor = await SeedDoctorAsync();

        var created = await CreateAsync(doctor, "  Indicaciones  ", "Su cita de {SpecialtyName}", Body);

        created.ShouldBe(new ReminderTemplateResponse(created.Id, "Indicaciones", "Su cita de {SpecialtyName}", Body, true, false));
        (await ReadAsync(created.Id)).ShouldBe(new TemplateRow(doctor.DoctorId, "Indicaciones", "Su cita de {SpecialtyName}", Body, true));
    }

    [Fact]
    public async Task Una_variable_no_permitida_se_rechaza_indicandola_y_no_se_guarda()
    {
        var doctor = await SeedDoctorAsync();

        using var response = await doctor.Client.PostAsJsonAsync(
            Url, new { name = "Indicaciones", subject = "Asunto", bodyTemplate = "Hola {Paciente}." });

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("ReminderTemplate.UnknownVariable");
        problem.GetProperty("title").GetString()!.ShouldStartWith("La variable {Paciente} no está permitida.");
        (await CountOwnAsync(doctor.DoctorId)).ShouldBe(0);
    }

    [Fact]
    public async Task El_medico_edita_su_plantilla()
    {
        var doctor = await SeedDoctorAsync();
        var created = await CreateAsync(doctor, "Indicaciones", "Asunto", Body);

        using var response = await doctor.Client.PutAsJsonAsync(
            $"{Url}/{created.Id}", new { name = "Ayuno", subject = "Ayuno para su cita", bodyTemplate = "Hola {PatientName}, venga en ayunas." });

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ReminderTemplateResponse>())!.Name.ShouldBe("Ayuno");
        (await ReadAsync(created.Id)).ShouldBe(
            new TemplateRow(doctor.DoctorId, "Ayuno", "Ayuno para su cita", "Hola {PatientName}, venga en ayunas.", true));
    }

    [Fact]
    public async Task El_medico_desactiva_su_plantilla_y_deja_de_aparecer_en_la_lista()
    {
        var doctor = await SeedDoctorAsync();
        var created = await CreateAsync(doctor, "Indicaciones", "Asunto", Body);

        using (var response = await doctor.Client.PatchAsync($"{Url}/{created.Id}/deactivate", content: null))
        {
            await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        (await ReadAsync(created.Id)).IsActive.ShouldBeFalse();
        (await ListAsync(doctor)).Items.ShouldNotContain(item => item.Id == created.Id);
        using var again = await doctor.Client.PatchAsync($"{Url}/{created.Id}/deactivate", content: null);
        await AssertErrorAsync(again, HttpStatusCode.Conflict, "ReminderTemplate.AlreadyInactive");
    }

    [Fact]
    public async Task La_lista_muestra_las_propias_activas_y_las_globales_como_solo_lectura_pero_no_las_de_otro_medico()
    {
        var doctor = await SeedDoctorAsync();
        var other = await SeedDoctorAsync();
        var own = await CreateAsync(doctor, "Indicaciones", "Asunto", Body);
        var others = await CreateAsync(other, "Ajena", "Asunto", Body);

        var page = await ListAsync(doctor);

        page.Items.ShouldContain(item => item.Id == own.Id && !item.IsReadOnly);
        page.Items.ShouldNotContain(item => item.Id == others.Id);
        page.Items.ShouldContain(item => item.Name == "Recordatorio de cita" && item.IsReadOnly);
        page.Items.Where(item => item.IsReadOnly).ShouldAllBe(item => item.IsActive);
        page.TotalCount.ShouldBe(page.Items.Count);
    }

    [Fact]
    public async Task Una_plantilla_global_no_se_edita_ni_se_desactiva()
    {
        var doctor = await SeedDoctorAsync();
        var globalId = await GlobalTemplateIdAsync();
        var before = await ReadAsync(globalId);

        using (var update = await doctor.Client.PutAsJsonAsync(
            $"{Url}/{globalId}", new { name = "Otro", subject = "Otro", bodyTemplate = "Otro" }))
        {
            await AssertErrorAsync(update, HttpStatusCode.Forbidden, "ReminderTemplate.GlobalReadOnly");
        }

        using var deactivate = await doctor.Client.PatchAsync($"{Url}/{globalId}/deactivate", content: null);
        await AssertErrorAsync(deactivate, HttpStatusCode.Forbidden, "ReminderTemplate.GlobalReadOnly");
        (await ReadAsync(globalId)).ShouldBe(before);
    }

    [Fact]
    public async Task La_plantilla_de_otro_medico_responde_como_inexistente()
    {
        var doctor = await SeedDoctorAsync();
        var other = await SeedDoctorAsync();
        var others = await CreateAsync(other, "Ajena", "Asunto", Body);

        using (var update = await doctor.Client.PutAsJsonAsync(
            $"{Url}/{others.Id}", new { name = "Mía", subject = "Asunto", bodyTemplate = Body }))
        {
            await AssertErrorAsync(update, HttpStatusCode.NotFound, "ReminderTemplate.NotFound");
        }

        using var deactivate = await doctor.Client.PatchAsync($"{Url}/{others.Id}/deactivate", content: null);
        await AssertErrorAsync(deactivate, HttpStatusCode.NotFound, "ReminderTemplate.NotFound");
        (await ReadAsync(others.Id)).ShouldBe(new TemplateRow(other.DoctorId, "Ajena", "Asunto", Body, true));
    }

    [Fact]
    public async Task Editar_la_plantilla_no_cambia_los_recordatorios_ya_programados_ni_los_enviados()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());
        var template = await CreateAsync(doctor, "Indicaciones", "Asunto", Body);
        var sentAt = CurrentHourUtc().AddHours(2);
        var pendingAt = CurrentHourUtc().AddHours(5);
        var original = await ScheduleFromPreviewAsync(doctor, appointmentId, template.Id, sentAt);
        await ScheduleFromPreviewAsync(doctor, appointmentId, template.Id, pendingAt);
        Clock.UtcNow = sentAt.AddSeconds(30);
        await RunJobAsync();

        using (var update = await doctor.Client.PutAsJsonAsync(
            $"{Url}/{template.Id}", new { name = "Indicaciones", subject = "Asunto", bodyTemplate = "Hola {PatientName}, texto nuevo." }))
        {
            await update.ShouldHaveStatusAsync(HttpStatusCode.OK);
        }

        (await MessagesAsync(appointmentId)).ShouldBe([(original, "sent"), (original, "pending")]);
        Factory.Notifications.Clear();
        Clock.UtcNow = pendingAt.AddSeconds(30);
        await RunJobAsync();
        Factory.Notifications.Sent.OfType<ReminderNotification>().ShouldHaveSingleItem().Message.ShouldBe(original);
    }

    [Fact]
    public async Task Un_paciente_no_gestiona_plantillas()
    {
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.PostAsJsonAsync(Url, new { name = "N", subject = "A", bodyTemplate = "C" });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    private static async Task<ReminderTemplateResponse> CreateAsync(DoctorSeed doctor, string name, string subject, string body)
    {
        using var response = await doctor.Client.PostAsJsonAsync(Url, new { name, subject, bodyTemplate = body });
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ReminderTemplateResponse>())!;
    }

    private static async Task<ListReminderTemplatesResponse> ListAsync(DoctorSeed doctor)
    {
        using var response = await doctor.Client.GetAsync($"{Url}?pageSize=100");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ListReminderTemplatesResponse>())!;
    }

    // Programa el texto de la vista previa, como lo haría el médico sin editarlo, y devuelve ese texto.
    private static async Task<string> ScheduleFromPreviewAsync(
        DoctorSeed doctor, int appointmentId, int templateId, DateTimeOffset scheduledAt)
    {
        string message;
        using (var preview = await doctor.Client.GetAsync(
            $"/api/doctors/me/appointments/{appointmentId}/reminders/preview?templateId={templateId}"))
        {
            await preview.ShouldHaveStatusAsync(HttpStatusCode.OK);
            message = (await preview.Content.ReadFromJsonAsync<ReminderPreviewResponse>())!.Message;
        }

        using var schedule = await doctor.Client.PostAsJsonAsync(
            $"/api/doctors/me/appointments/{appointmentId}/reminders", new { templateId, message, scheduledAt });
        await schedule.ShouldHaveStatusAsync(HttpStatusCode.Created);
        return message;
    }

    private Task RunJobAsync() => Factory.GetJob<SendDueRemindersJob>().RunOnceAsync(CancellationToken.None);

    // Las columnas van en snake_case porque SqlQuery aplica la misma convención de nombres que el contexto.
    private async Task<TemplateRow> ReadAsync(int id)
    {
        TemplateRow? row = null;
        await WithContextAsync(async context => row = (await context.Database.SqlQuery<TemplateRow>(
            $"""
            SELECT doctor_id, name, subject, body_template, is_active
            FROM messaging.reminder_template
            WHERE id = {id}
            """).ToListAsync()).Single());
        return row!;
    }

    private async Task<int> CountOwnAsync(int doctorId)
    {
        var count = 0;
        await WithContextAsync(async context => count = (await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM messaging.reminder_template WHERE doctor_id = {doctorId}").ToListAsync()).Single());
        return count;
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

    private async Task<List<(string Message, string Status)>> MessagesAsync(int appointmentId)
    {
        List<(string, string)> rows = [];
        await WithContextAsync(async context => rows = (await context.Database.SqlQuery<MessageRow>(
                $"""
                SELECT message_body, delivery_status
                FROM messaging.reminder
                WHERE appointment_id = {appointmentId}
                ORDER BY id
                """).ToListAsync())
            .Select(row => (row.MessageBody, row.DeliveryStatus)).ToList());
        return rows;
    }

    private sealed record MessageRow(string MessageBody, string DeliveryStatus);
}
