using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Messaging;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Pasos de las plantillas de recordatorio. El médico inicia sesión con el cliente del escenario; las plantillas de
/// otro médico y la cita se siembran en la base.
/// </summary>
[Binding]
public sealed class PlantillasDeRecordatorioSteps(ApiScenario scenario)
{
    private const string Url = "/api/doctors/me/reminder-templates";
    private const int AppointmentHours = 2;

    private int _doctorUserId;
    private ReminderTemplateResponse? _own;
    private int _othersId;
    private int _appointmentId;
    private HttpStatusCode _status;
    private JsonElement _problem;

    [Given("que inicié sesión como médico para gestionar plantillas")]
    public Task DadoQueInicieSesion() =>
        AuthenticatedScenario.LoginAsync(scenario, "Doctor", "plantillas-medico", userId => _doctorUserId = userId);

    [Given("que creé la plantilla {string} con el asunto {string} y el cuerpo {string}")]
    [When("creo la plantilla {string} con el asunto {string} y el cuerpo {string}")]
    public async Task CuandoCreoLaPlantilla(string name, string subject, string body)
    {
        using var response = await scenario.Client.PostAsJsonAsync(Url, new { name, subject, bodyTemplate = body });
        await RecordAsync(response);
        if (response.StatusCode == HttpStatusCode.Created)
            _own = await response.Content.ReadFromJsonAsync<ReminderTemplateResponse>();
    }

    [Given("que otro médico tiene la plantilla {string}")]
    public async Task DadoQueOtroMedicoTieneLaPlantilla(string name)
    {
        var other = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"plantillas-otro-{Guid.NewGuid():N}@correo.com").Value);
        await scenario.Factory.SaveAsync(other);
        _othersId = await WithContextAsync(async context =>
        {
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, other.Id);
            var template = ReminderTemplate.Create(doctorId, name, "Asunto", "Hola {PatientName}.").Value;
            context.Set<ReminderTemplate>().Add(template);
            await context.SaveChangesAsync();
            return template.Id;
        });
    }

    [Given("que programé un recordatorio con mi plantilla para una cita confirmada")]
    public async Task DadoQueProgrameUnRecordatorio()
    {
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        var startsAt = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddDays(10);
        var patient = UserMother.WithRole(UserRole.Patient, email: Email.Create($"plantillas-paciente-{Guid.NewGuid():N}@correo.com").Value);
        await scenario.Factory.SaveAsync(patient);
        _appointmentId = await WithContextAsync(async context =>
        {
            var doctorId = await context.Database.SqlQuery<int>(
                $"SELECT id AS \"Value\" FROM people.doctor WHERE user_id = {_doctorUserId}").SingleAsync();
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            await TestDataSeeder.InsertPatientProfileAsync(context, patient.Id);
            var patientId = await context.Patients.Where(value => value.UserId == patient.Id).Select(value => value.Id).SingleAsync();
            // El bloque cubre exactamente la cita, como exige el trigger de coherencia de la cita.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({doctorId}, NULL, {startsAt}, {startsAt.AddHours(AppointmentHours)}, true)
                """);
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, doctorId, specialtyId, startsAt, AppointmentHours, "confirmed");
        });

        string message;
        using (var preview = await scenario.Client.GetAsync(
            $"/api/doctors/me/appointments/{_appointmentId}/reminders/preview?templateId={_own!.Id}"))
        {
            await preview.ShouldHaveStatusAsync(HttpStatusCode.OK);
            message = (await preview.Content.ReadFromJsonAsync<ReminderPreviewResponse>())!.Message;
        }

        using var schedule = await scenario.Client.PostAsJsonAsync(
            $"/api/doctors/me/appointments/{_appointmentId}/reminders",
            new { templateId = _own.Id, message, scheduledAt = now.AddDays(1) });
        await schedule.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [When("edito mi plantilla con el cuerpo {string}")]
    public Task CuandoEditoMiPlantilla(string body) =>
        PutAsync(_own!.Id, new { name = _own.Name, subject = _own.Subject, bodyTemplate = body });

    [When("desactivo mi plantilla")]
    public Task CuandoDesactivoMiPlantilla() => DeactivateAsync(_own!.Id);

    [When("edito la plantilla global {string}")]
    public async Task CuandoEditoLaGlobal(string name) =>
        await PutAsync(await GlobalIdAsync(name), new { name = "Otro", subject = "Otro", bodyTemplate = "Otro" });

    [When("desactivo la plantilla global {string}")]
    public async Task CuandoDesactivoLaGlobal(string name) => await DeactivateAsync(await GlobalIdAsync(name));

    [When("edito la plantilla de otro médico")]
    public Task CuandoEditoLaDeOtroMedico() =>
        PutAsync(_othersId, new { name = "Mía", subject = "Asunto", bodyTemplate = "Hola {PatientName}." });

    [Then("mi plantilla aparece activa en mi lista")]
    public async Task EntoncesMiPlantillaApareceActiva()
    {
        _status.ShouldBe(HttpStatusCode.Created);
        var item = (await ListAsync()).Items.Single(template => template.Id == _own!.Id);
        item.IsActive.ShouldBeTrue();
        item.IsReadOnly.ShouldBeFalse();
    }

    [Then("mi plantilla tiene el cuerpo {string}")]
    public async Task EntoncesMiPlantillaTieneElCuerpo(string body)
    {
        _status.ShouldBe(HttpStatusCode.OK);
        (await ListAsync()).Items.Single(template => template.Id == _own!.Id).BodyTemplate.ShouldBe(body);
    }

    [Then("mi plantilla ya no aparece en mi lista")]
    public async Task EntoncesMiPlantillaYaNoAparece()
    {
        _status.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync()).Items.ShouldNotContain(template => template.Id == _own!.Id);
    }

    [Then("la plantilla global {string} aparece en mi lista como de solo lectura")]
    public async Task EntoncesLaGlobalApareceComoSoloLectura(string name) =>
        (await ListAsync()).Items.Single(template => template.Name == name).IsReadOnly.ShouldBeTrue();

    [Then("la plantilla de otro médico no aparece en mi lista")]
    public async Task EntoncesLaDeOtroMedicoNoAparece() =>
        (await ListAsync()).Items.ShouldNotContain(template => template.Id == _othersId);

    [Then("la operación de plantilla se rechaza con el estado {int} y el código {string}")]
    public void EntoncesSeRechaza(int status, string code)
    {
        ((int)_status).ShouldBe(status);
        _problem.GetProperty("code").GetString().ShouldBe(code);
    }

    [Then("el mensaje indica la variable {string}")]
    public void EntoncesElMensajeIndicaLaVariable(string variable) =>
        _problem.GetProperty("title").GetString()!.ShouldStartWith($"La variable {variable} no está permitida.");

    [Then("el recordatorio programado conserva el texto {string}")]
    public async Task EntoncesElRecordatorioConservaElTexto(string message)
    {
        _status.ShouldBe(HttpStatusCode.OK);
        var stored = await WithContextAsync(context => context.Database.SqlQuery<string>(
            $"SELECT message_body AS \"Value\" FROM messaging.reminder WHERE appointment_id = {_appointmentId}").SingleAsync());
        stored.ShouldBe(message);
    }

    private async Task PutAsync(int id, object body)
    {
        using var response = await scenario.Client.PutAsJsonAsync($"{Url}/{id}", body);
        await RecordAsync(response);
    }

    private async Task DeactivateAsync(int id)
    {
        using var response = await scenario.Client.PatchAsync($"{Url}/{id}/deactivate", content: null);
        await RecordAsync(response);
    }

    // El cuerpo de la respuesta solo se puede leer una vez: el error se guarda aquí.
    private async Task RecordAsync(HttpResponseMessage response)
    {
        _status = response.StatusCode;
        if (!response.IsSuccessStatusCode)
            _problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<ListReminderTemplatesResponse> ListAsync()
    {
        using var response = await scenario.Client.GetAsync($"{Url}?pageSize=100");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ListReminderTemplatesResponse>())!;
    }

    private Task<int> GlobalIdAsync(string name) =>
        WithContextAsync(context => context.Database.SqlQuery<int>(
            $"SELECT id AS \"Value\" FROM messaging.reminder_template WHERE doctor_id IS NULL AND name = {name}").SingleAsync());

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
