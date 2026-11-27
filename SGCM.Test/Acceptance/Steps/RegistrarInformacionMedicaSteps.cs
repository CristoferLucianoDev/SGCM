using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Pasos del registro de información clínica. El médico inicia sesión con la hora real y el reloj de la app no se
/// mueve: las citas se siembran con <see cref="TestDataSeeder"/> relativas a la hora en punto actual.
/// </summary>
[Binding]
public sealed class RegistrarInformacionMedicaSteps(ApiScenario scenario) : IDisposable
{
    private const string DoctorAppointmentsUrl = "/api/doctors/me/appointments";
    private const string StatusReason = "Motivo registrado en la prueba";
    private const int AppointmentHours = 2;
    private const int BlockMarginHours = 1;

    // Los estados y los tipos de acceso del escenario se escriben en español; la base los guarda en inglés.
    private static readonly Dictionary<string, string> StatusBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pendiente"] = "pending",
        ["confirmada"] = "confirmed",
        ["completada"] = "completed",
        ["cancelada"] = "cancelled",
        ["rechazada"] = "rejected"
    };

    private static readonly Dictionary<string, string> AccessTypeBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["creación"] = "create",
        ["corrección"] = "update"
    };

    private int _userId;
    private int _doctorId;
    private int _appointmentId;
    private JsonDocument? _body;

    [Given("que inicié sesión como médico para registrar información clínica")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "registro-clinico-medico", id => _userId = id);
        _doctorId = await DoctorIdOfAsync(_userId);
    }

    [Given("que tengo una cita {string} que empezó hace {int} hora(s)")]
    public async Task DadoQueTengoUnaCitaQueEmpezo(string spanishStatus, int hoursSinceStart) =>
        _appointmentId = await SeedAppointmentAsync(_doctorId, spanishStatus, CurrentHour().AddHours(-hoursSinceStart));

    [Given("que tengo una cita {string} que empieza dentro de {int} hora(s)")]
    public async Task DadoQueTengoUnaCitaQueEmpieza(string spanishStatus, int hoursUntilStart) =>
        _appointmentId = await SeedAppointmentAsync(_doctorId, spanishStatus, CurrentHour().AddHours(hoursUntilStart));

    [Given("que otro médico tiene una cita {string} que empezó hace {int} hora(s)")]
    public async Task DadoQueOtroMedicoTieneUnaCita(string spanishStatus, int hoursSinceStart)
    {
        var otherDoctorId = await SeedOtherDoctorAsync();
        _appointmentId = await SeedAppointmentAsync(otherDoctorId, spanishStatus, CurrentHour().AddHours(-hoursSinceStart));
    }

    [Given("que ya registré el diagnóstico {string} en esa cita")]
    public async Task DadoQueYaRegistreElDiagnostico(string diagnosis)
    {
        await PostAsync(new { diagnosis });
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [When("registro el diagnóstico {string} y el tratamiento {string}")]
    public Task CuandoRegistroElDiagnostico(string diagnosis, string treatment) => PostAsync(new { diagnosis, treatment });

    [Then("el registro clínico se crea como primera versión")]
    public Task EntoncesSeCreaComoPrimeraVersion() => AssertCreatedAsync(isCorrection: false);

    [Then("el registro clínico se crea como corrección")]
    public Task EntoncesSeCreaComoCorreccion() => AssertCreatedAsync(isCorrection: true);

    [Then("la cita tiene {int} versión/versiones de registro clínico")]
    public async Task EntoncesLaCitaTieneVersiones(int count) => (await ReadDiagnosesAsync()).Count.ShouldBe(count);

    [Then("la versión vigente tiene el diagnóstico {string}")]
    public async Task EntoncesLaVersionVigenteTieneElDiagnostico(string diagnosis) =>
        (await WithContextAsync(context => context.Database.SqlQuery<string>(
            $"""
            SELECT diagnosis AS "Value" FROM clinical.vw_medical_record_current WHERE appointment_id = {_appointmentId}
            """).SingleAsync())).ShouldBe(diagnosis);

    [Then("la primera versión conserva el diagnóstico {string}")]
    public async Task EntoncesLaPrimeraVersionConserva(string diagnosis) =>
        (await ReadDiagnosesAsync())[0].ShouldBe(diagnosis);

    [Then("el registro de accesos tiene una {string} a mi nombre")]
    public async Task EntoncesElRegistroDeAccesosTiene(string spanishAccessType)
    {
        var versionId = _body!.RootElement.GetProperty("id").GetInt32();
        var accesses = await WithContextAsync(context => context.Database.SqlQuery<AccessRow>(
            $"""
            SELECT accessed_by, access_type FROM clinical.medical_record_access_log WHERE medical_record_id = {versionId}
            """).ToListAsync());

        accesses.ShouldBe([new AccessRow(_userId, AccessTypeBySpanishName[spanishAccessType])]);
    }

    [Then("el registro clínico se rechaza con el estado {int} y el código {string}")]
    public async Task EntoncesSeRechaza(int status, string code)
    {
        await scenario.Response!.ShouldHaveStatusAsync((HttpStatusCode)status);
        await ReadBodyAsync();

        _body!.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    [Then("la cita no tiene registro clínico ni accesos registrados")]
    public async Task EntoncesLaCitaNoTieneRegistro()
    {
        (await ReadDiagnosesAsync()).ShouldBeEmpty();
        var accesses = await WithContextAsync(context => context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM clinical.medical_record_access_log log
            JOIN clinical.medical_record record ON record.id = log.medical_record_id
            WHERE record.appointment_id = {_appointmentId}
            """).SingleAsync());
        accesses.ShouldBe(0);
    }

    /// <summary>Libera el documento de la última respuesta; la respuesta y el cliente los libera el escenario.</summary>
    public void Dispose() => _body?.Dispose();

    private async Task AssertCreatedAsync(bool isCorrection)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.Created);
        await ReadBodyAsync();

        _body!.RootElement.GetProperty("appointmentId").GetInt32().ShouldBe(_appointmentId);
        _body.RootElement.GetProperty("isCorrection").GetBoolean().ShouldBe(isCorrection);
    }

    private async Task PostAsync(object body)
    {
        scenario.Response?.Dispose();
        _body?.Dispose();
        _body = null;
        scenario.Response = await scenario.Client.PostAsJsonAsync(
            $"{DoctorAppointmentsUrl}/{_appointmentId}/medical-record", body);
    }

    private async Task ReadBodyAsync() =>
        _body ??= JsonDocument.Parse(await scenario.Response!.Content.ReadAsStringAsync());

    /// <summary>Hora en punto actual en UTC según el reloj de la app, truncada.</summary>
    private DateTimeOffset CurrentHour()
    {
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Paciente nuevo con una cita del médico en el estado pedido, dentro de un bloque que la cubre con margen. La
    /// especialidad es nueva y se asocia al médico.
    /// </summary>
    private async Task<int> SeedAppointmentAsync(int doctorId, string spanishStatus, DateTimeOffset startsAt)
    {
        var status = StatusBySpanishName[spanishStatus];
        var statusReason = status is "cancelled" or "rejected" ? StatusReason : null;
        var email = $"registro-clinico-paciente-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);

        return await WithContextAsync(async context =>
        {
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({doctorId}, NULL, {startsAt.AddHours(-BlockMarginHours)},
                        {startsAt.AddHours(AppointmentHours + BlockMarginHours)}, true)
                """);
            await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
            var patientId = await context.Patients.Where(patient => patient.UserId == user.Id)
                .Select(patient => patient.Id).SingleAsync();

            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, doctorId, specialtyId, startsAt, AppointmentHours, status, statusReason);
        });
    }

    /// <summary>Otro médico con perfil completo, sin sesión: solo es dueño de la cita.</summary>
    private async Task<int> SeedOtherDoctorAsync()
    {
        var email = $"registro-clinico-otro-medico-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);

        return await WithContextAsync(context => TestDataSeeder.InsertDoctorProfileAsync(context, user.Id));
    }

    private Task<int> DoctorIdOfAsync(int userId) => WithContextAsync(context =>
        context.Doctors.Where(doctor => doctor.UserId == userId).Select(doctor => doctor.Id).SingleAsync());

    private Task<List<string?>> ReadDiagnosesAsync() => WithContextAsync(context => context.Database.SqlQuery<string?>(
        $"""
        SELECT diagnosis AS "Value" FROM clinical.medical_record WHERE appointment_id = {_appointmentId} ORDER BY id
        """).ToListAsync());

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private sealed record AccessRow(int AccessedBy, string AccessType);
}
