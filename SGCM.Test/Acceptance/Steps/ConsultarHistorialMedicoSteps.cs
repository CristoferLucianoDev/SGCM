using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Pasos de la consulta del historial médico. Quien consulta inicia sesión con la hora real y el reloj de la app no se
/// mueve: las citas se siembran en el pasado, relativas a la hora en punto actual, y las versiones del registro se
/// guardan directamente con sus horas de creación. "El paciente" es el de la sesión cuando consulta un paciente, y uno
/// nuevo cuando consulta un médico.
/// </summary>
[Binding]
public sealed class ConsultarHistorialMedicoSteps(ApiScenario scenario) : IDisposable
{
    private const string PatientHistoryUrl = "/api/patients/me/medical-history";
    private const string DoctorPatientsUrl = "/api/doctors/me/patients";
    private const string Completed = "completed";
    private const int AppointmentHours = 2;
    private const int BlockMarginHours = 1;

    private readonly Dictionary<int, int> _appointmentByDaysAgo = [];
    private int _userId;
    private int? _doctorId;
    private int? _patientId;
    private JsonDocument? _body;

    [Given("que inicié sesión como paciente para consultar mi historial médico")]
    public async Task DadoQueInicieSesionComoPaciente()
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Patient", "historial-paciente", id => _userId = id);
        _patientId = await WithContextAsync(context => context.Patients.Where(patient => patient.UserId == _userId)
            .Select(patient => patient.Id).SingleAsync());
    }

    [Given("que inicié sesión como médico para consultar historiales médicos")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "historial-medico", id => _userId = id);
        _doctorId = await WithContextAsync(context => context.Doctors.Where(doctor => doctor.UserId == _userId)
            .Select(doctor => doctor.Id).SingleAsync());
    }

    [Given("que el paciente tuvo una cita completada hace {int} día(s) con el diagnóstico {string} corregido a {string}")]
    public async Task DadoQueElPacienteTuvoUnaCitaConRegistro(int daysAgo, string original, string corrected)
    {
        var (doctorId, doctorUserId) = await SeedOtherDoctorAsync();
        await SeedRecordedAppointmentAsync(daysAgo, doctorId, doctorUserId, original, corrected);
    }

    [Given("que el paciente tuvo una cita completada hace {int} día(s) sin registro clínico")]
    public async Task DadoQueElPacienteTuvoUnaCitaSinRegistro(int daysAgo)
    {
        var (doctorId, _) = await SeedOtherDoctorAsync();
        await SeedAppointmentAsync(daysAgo, doctorId);
    }

    [Given("que atendí al paciente hace {int} día(s) con el diagnóstico {string} corregido a {string}")]
    public Task DadoQueAtendiAlPaciente(int daysAgo, string original, string corrected) =>
        SeedRecordedAppointmentAsync(daysAgo, _doctorId!.Value, _userId, original, corrected);

    [When("consulto mi historial médico")]
    public Task CuandoConsultoMiHistorial() => GetAsync(PatientHistoryUrl);

    [When("consulto el historial médico del paciente")]
    public async Task CuandoConsultoElHistorialDelPaciente() =>
        await GetAsync($"{DoctorPatientsUrl}/{await PatientIdAsync()}/medical-history");

    [Then("el historial médico tiene {int} cita(s)")]
    public async Task EntoncesElHistorialTieneCitas(int count)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await EntriesAsync()).Count.ShouldBe(count);
    }

    [Then("la cita de hace {int} día(s) muestra el diagnóstico vigente {string} sin versiones anteriores")]
    public async Task EntoncesLaCitaMuestraSinAnteriores(int daysAgo, string diagnosis)
    {
        var entry = await EntryAsync(daysAgo);
        Current(entry).GetProperty("diagnosis").GetString().ShouldBe(diagnosis);
        entry.GetProperty("previousVersions").GetArrayLength().ShouldBe(0);
    }

    [Then("la cita de hace {int} día(s) muestra el diagnóstico vigente {string} con la versión anterior {string}")]
    public async Task EntoncesLaCitaMuestraConAnterior(int daysAgo, string diagnosis, string previous)
    {
        var entry = await EntryAsync(daysAgo);
        Current(entry).GetProperty("diagnosis").GetString().ShouldBe(diagnosis);
        entry.GetProperty("previousVersions").EnumerateArray()
            .Select(version => version.GetProperty("diagnosis").GetString()).ShouldBe([previous]);
    }

    [Then("la cita de hace {int} día(s) indica que no tiene registro clínico")]
    public async Task EntoncesLaCitaNoTieneRegistro(int daysAgo)
    {
        var entry = await EntryAsync(daysAgo);
        entry.GetProperty("hasMedicalRecord").GetBoolean().ShouldBeFalse();
        entry.GetProperty("currentVersion").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Then("el registro de accesos tiene {int} consulta(s) a mi nombre")]
    public async Task EntoncesElRegistroDeAccesosTiene(int count) =>
        (await WithContextAsync(context => context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM clinical.medical_record_access_log
            WHERE accessed_by = {_userId} AND access_type = 'view'
            """).SingleAsync())).ShouldBe(count);

    [Then("la consulta del historial se rechaza con el estado {int} y el código {string}")]
    public async Task EntoncesLaConsultaSeRechaza(int status, string code)
    {
        await scenario.Response!.ShouldHaveStatusAsync((HttpStatusCode)status);
        (await BodyAsync()).RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    /// <summary>Libera el documento de la última respuesta; la respuesta y el cliente los libera el escenario.</summary>
    public void Dispose() => _body?.Dispose();

    private async Task GetAsync(string url)
    {
        scenario.Response?.Dispose();
        _body?.Dispose();
        _body = null;
        scenario.Response = await scenario.Client.GetAsync(url);
    }

    private async Task<JsonDocument> BodyAsync() =>
        _body ??= JsonDocument.Parse(await scenario.Response!.Content.ReadAsStringAsync());

    private async Task<List<JsonElement>> EntriesAsync() =>
        (await BodyAsync()).RootElement.GetProperty("entries").EnumerateArray().ToList();

    private async Task<JsonElement> EntryAsync(int daysAgo)
    {
        var appointmentId = _appointmentByDaysAgo[daysAgo];
        return (await EntriesAsync()).Single(entry => entry.GetProperty("appointmentId").GetInt32() == appointmentId);
    }

    private static JsonElement Current(JsonElement entry)
    {
        entry.GetProperty("hasMedicalRecord").GetBoolean().ShouldBeTrue();
        return entry.GetProperty("currentVersion");
    }

    /// <summary>Cita completada con un registro corregido una vez: la versión original y, dos horas después, la nueva.</summary>
    private async Task SeedRecordedAppointmentAsync(
        int daysAgo, int doctorId, int doctorUserId, string original, string corrected)
    {
        var (appointmentId, startsAt) = await SeedAppointmentAsync(daysAgo, doctorId);
        var patientId = await PatientIdAsync();
        await WithContextAsync(async context =>
        {
            var first = MedicalRecord.Create(
                appointmentId, patientId, Content(original), doctorUserId, startsAt.AddHours(1));
            context.AddRange(first, first.Correct(Content(corrected), doctorUserId, startsAt.AddHours(3)));
            return await context.SaveChangesAsync();
        });
    }

    /// <summary>Cita completada del paciente con el médico indicado, dentro de un bloque que la cubre con margen.</summary>
    private async Task<(int AppointmentId, DateTimeOffset StartsAt)> SeedAppointmentAsync(int daysAgo, int doctorId)
    {
        var startsAt = CurrentHour().AddDays(-daysAgo);
        var patientId = await PatientIdAsync();
        var appointmentId = await WithContextAsync(async context =>
        {
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({doctorId}, NULL, {startsAt.AddHours(-BlockMarginHours)},
                        {startsAt.AddHours(AppointmentHours + BlockMarginHours)}, true)
                """);
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, doctorId, specialtyId, startsAt, AppointmentHours, Completed);
        });
        _appointmentByDaysAgo[daysAgo] = appointmentId;
        return (appointmentId, startsAt);
    }

    /// <summary>El paciente de la sesión, o uno nuevo con perfil si consulta un médico.</summary>
    private async Task<int> PatientIdAsync()
    {
        if (_patientId is { } patientId) return patientId;

        var user = UserMother.WithRole(
            UserRole.Patient, email: Email.Create($"historial-paciente-{Guid.NewGuid():N}@correo.com").Value);
        await scenario.Factory.SaveAsync(user);
        _patientId = await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
            return await context.Patients.Where(patient => patient.UserId == user.Id)
                .Select(patient => patient.Id).SingleAsync();
        });
        return _patientId.Value;
    }

    /// <summary>Otro médico con perfil completo, sin sesión: solo atiende citas del paciente.</summary>
    private async Task<(int DoctorId, int UserId)> SeedOtherDoctorAsync()
    {
        var user = UserMother.WithRole(
            UserRole.Doctor, email: Email.Create($"historial-otro-medico-{Guid.NewGuid():N}@correo.com").Value);
        await scenario.Factory.SaveAsync(user);
        var doctorId = await WithContextAsync(context => TestDataSeeder.InsertDoctorProfileAsync(context, user.Id));
        return (doctorId, user.Id);
    }

    private static MedicalRecordContent Content(string diagnosis) =>
        MedicalRecordContent.Create(diagnosis, null, null).Value;

    /// <summary>Hora en punto actual en UTC según el reloj de la app, truncada.</summary>
    private DateTimeOffset CurrentHour()
    {
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
    }

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
