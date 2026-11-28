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
using SGCM.Web.Contracts.Messaging;
using SGCM.Web.Jobs;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Pasos de los recordatorios que programa el médico. El médico inicia sesión y la cita es de un paciente nuevo; el
/// envío lo hace el job con el FakeDateTimeProvider de la fábrica.
/// </summary>
[Binding]
public sealed class ProgramarRecordatoriosSteps(ApiScenario scenario)
{
    private const string Url = "/api/doctors/me/appointments";
    private const int AppointmentHours = 2;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);

    private sealed record ReminderRow(int? TemplateId, string MessageBody, DateTimeOffset ScheduledAt, string DeliveryStatus);

    private int _doctorUserId;
    private int _specialtyId;
    private int _appointmentId;
    private int _templateId;
    private string _patientEmail = string.Empty;
    private DateTimeOffset _startsAt;
    private DateTimeOffset _scheduledAt;
    private ReminderPreviewResponse? _preview;
    private string _savedMessage = string.Empty;
    private HttpStatusCode _status;
    private string? _code;

    [Given("que inicié sesión como médico con una cita {string} dentro de {int} días a las {string}")]
    public async Task DadoQueTengoUnaCita(string status, int days, string time)
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "programar-medico", userId => _doctorUserId = userId);
        _startsAt = Local(Today().AddDays(days), time);
        _patientEmail = $"programar-paciente-{Guid.NewGuid():N}@correo.com";
        var patient = UserMother.WithRole(UserRole.Patient, email: Email.Create(_patientEmail).Value);
        await scenario.Factory.SaveAsync(patient);
        await WithContextAsync(async context =>
        {
            var doctorId = await DoctorIdAsync(context, _doctorUserId);
            _specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, _specialtyId);
            await TestDataSeeder.InsertPatientProfileAsync(context, patient.Id);
            var patientId = await context.Patients.Where(value => value.UserId == patient.Id).Select(value => value.Id).SingleAsync();
            await InsertBlockAsync(context, doctorId);
            _appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, doctorId, _specialtyId, _startsAt.ToUniversalTime(), AppointmentHours, status);
            return 0;
        });
    }

    [Given("que la cita es de otro médico")]
    public async Task DadoQueLaCitaEsDeOtroMedico()
    {
        var other = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"programar-otro-{Guid.NewGuid():N}@correo.com").Value);
        await scenario.Factory.SaveAsync(other);
        await WithContextAsync(async context =>
        {
            var otherDoctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, other.Id);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, otherDoctorId, _specialtyId);
            await InsertBlockAsync(context, otherDoctorId);
            // La misma cita pasa al otro médico: el médico de la sesión ya no es su dueño.
            return await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE scheduling.appointment SET doctor_id = {otherDoctorId} WHERE id = {_appointmentId}");
        });
    }

    [Given("que tengo una plantilla propia con el texto {string}")]
    public async Task DadoQueTengoUnaPlantillaPropia(string body) =>
        _templateId = await WithContextAsync(async context => (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO messaging.reminder_template (doctor_id, name, subject, body_template)
            VALUES ({await DoctorIdAsync(context, _doctorUserId)}, 'Indicaciones', 'Indicaciones para su cita', {body})
            RETURNING id AS "Value"
            """).ToListAsync()).Single());

    [Given("que programé el mensaje de la vista previa de mi plantilla para mañana a las {string}")]
    public async Task DadoQueProgrameLaVistaPreviaDeMiPlantilla(string time)
    {
        _preview = await PreviewAsync(_templateId);
        await ScheduleAsync(new { templateId = _templateId, message = _preview.Message, scheduledAt = Tomorrow(time) });
        _status.ShouldBe(HttpStatusCode.Created);
        _savedMessage = _preview.Message;
    }

    [When("pido la vista previa de la plantilla global para esa cita")]
    public async Task CuandoPidoLaVistaPreviaGlobal()
    {
        _templateId = await WithContextAsync(context => context.Database.SqlQuery<int>(
            $"""
            SELECT id AS "Value" FROM messaging.reminder_template
            WHERE doctor_id IS NULL AND name = 'Recordatorio de cita'
            """).SingleAsync());
        _preview = await PreviewAsync(_templateId);
    }

    [When("programo el mensaje de la vista previa con el agregado {string} para mañana a las {string}")]
    public async Task CuandoProgramoLaVistaPreviaEditada(string addition, string time)
    {
        _savedMessage = _preview!.Message + addition;
        await ScheduleAsync(new { templateId = _templateId, message = _savedMessage, scheduledAt = Tomorrow(time) });
        _status.ShouldBe(HttpStatusCode.Created);
    }

    [Given("que programé el mensaje libre {string} para mañana a las {string}")]
    [When("programo el mensaje libre {string} para mañana a las {string}")]
    public Task CuandoProgramoElMensajeLibre(string message, string time)
    {
        _savedMessage = message;
        return ScheduleAsync(new { message, scheduledAt = Tomorrow(time) });
    }

    [When("programo el mensaje libre {string} para hace {int} hora(s)")]
    public Task CuandoProgramoEnElPasado(string message, int hours) =>
        ScheduleAsync(new { message, scheduledAt = scenario.Factory.Clock.UtcNow.AddHours(-hours) });

    [When("cambio el texto de mi plantilla a {string}")]
    public Task CuandoCambioMiPlantilla(string body) =>
        WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE messaging.reminder_template SET body_template = {body} WHERE id = {_templateId}"));

    [When("llega la hora del recordatorio programado y corre el envío de recordatorios")]
    public async Task CuandoLlegaLaHora()
    {
        scenario.Factory.Notifications.Clear();
        scenario.Factory.Clock.UtcNow = _scheduledAt.ToUniversalTime().AddSeconds(30);
        await scenario.Factory.GetJob<SendDueRemindersJob>().RunOnceAsync(CancellationToken.None);
    }

    [Then("el mensaje muestra la fecha y la hora reales de la cita sin variables")]
    public void EntoncesElMensajeMuestraLosDatosReales()
    {
        _preview!.Message.ShouldContain($"el {_startsAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} a las {_startsAt:HH:mm}");
        _preview.Message.ShouldNotContain("{");
        _preview.Subject.ShouldNotContain("{");
    }

    [Then("queda un recordatorio pendiente con el texto editado y la plantilla como referencia")]
    public async Task EntoncesQuedaConElTextoEditado()
    {
        var reminder = (await RemindersAsync()).ShouldHaveSingleItem();
        reminder.ShouldBe(new ReminderRow(_templateId, _savedMessage, _scheduledAt, "pending"));
    }

    [Then("queda un recordatorio pendiente con el texto {string} sin plantilla")]
    public async Task EntoncesQuedaSinPlantilla(string message)
    {
        _status.ShouldBe(HttpStatusCode.Created);
        (await RemindersAsync()).ShouldHaveSingleItem().ShouldBe(new ReminderRow(null, message, _scheduledAt, "pending"));
    }

    [Then("el paciente recibe el recordatorio {string}")]
    public void EntoncesElPacienteRecibe(string message)
    {
        var sent = scenario.Factory.Notifications.Sent.OfType<ReminderNotification>().ShouldHaveSingleItem();
        sent.Recipient.Email.ShouldBe(_patientEmail);
        sent.Message.ShouldBe(message);
    }

    [Then("el paciente recibe el recordatorio con el texto que se guardó al programarlo")]
    public void EntoncesElPacienteRecibeElTextoGuardado()
    {
        _savedMessage.ShouldBe("Hola Ana Pérez, traiga sus estudios.");
        EntoncesElPacienteRecibe(_savedMessage);
    }

    [Then("el recordatorio programado queda registrado como {string}")]
    public async Task EntoncesQuedaRegistradoComo(string status) =>
        (await RemindersAsync()).ShouldHaveSingleItem().DeliveryStatus.ShouldBe(status);

    [Then("la programación se rechaza con el estado {int} y el código {string}")]
    public async Task EntoncesSeRechaza(int status, string code)
    {
        ((int)_status).ShouldBe(status);
        _code.ShouldBe(code);
        (await RemindersAsync()).ShouldBeEmpty();
    }

    private async Task<ReminderPreviewResponse> PreviewAsync(int templateId)
    {
        using var response = await scenario.Client.GetAsync($"{Url}/{_appointmentId}/reminders/preview?templateId={templateId}");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ReminderPreviewResponse>())!;
    }

    // El cuerpo de la respuesta solo se puede leer una vez: se guardan el estado y el código del error.
    private async Task ScheduleAsync(object body)
    {
        using var response = await scenario.Client.PostAsJsonAsync($"{Url}/{_appointmentId}/reminders", body);
        _status = response.StatusCode;
        _code = response.IsSuccessStatusCode ? null : await response.ReadErrorCodeAsync();
    }

    private DateTimeOffset Tomorrow(string time) => _scheduledAt = Local(Today().AddDays(1), time);

    // Las columnas van en snake_case porque SqlQuery aplica la misma convención de nombres que el contexto.
    private Task<List<ReminderRow>> RemindersAsync() =>
        WithContextAsync(context => context.Database.SqlQuery<ReminderRow>(
            $"""
            SELECT template_id, message_body, scheduled_at, delivery_status
            FROM messaging.reminder
            WHERE appointment_id = {_appointmentId}
            ORDER BY id
            """).ToListAsync());

    // El bloque cubre exactamente la cita, como exige el trigger de coherencia de la cita.
    private Task<int> InsertBlockAsync(AppDbContext context, int doctorId)
    {
        var startsAtUtc = _startsAt.ToUniversalTime();
        return context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctorId}, NULL, {startsAtUtc}, {startsAtUtc.AddHours(AppointmentHours)}, true)
            """);
    }

    private static Task<int> DoctorIdAsync(AppDbContext context, int userId) =>
        context.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM people.doctor WHERE user_id = {userId}").SingleAsync();

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private DateOnly Today() => DateOnly.FromDateTime(scenario.Factory.Clock.UtcNow.ToOffset(SantoDomingo).DateTime);

    private static DateTimeOffset Local(DateOnly date, string time) =>
        new(date.ToDateTime(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture)), SantoDomingo);
}
