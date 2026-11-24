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
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class CierreDeBloqueSteps(ApiScenario scenario)
{
    private const string BlocksUrl = "/api/doctors/me/availability-blocks";
    private const int AppointmentHours = 2;

    private sealed record PatientAppointment(string Email, int AppointmentId);

    private readonly List<PatientAppointment> _appointments = [];
    private int _doctorUserId;
    private int _doctorId;
    private int _specialtyId;
    private int _blockId;
    private DateOnly _day;
    private DateTimeOffset _blockStart;
    private JsonElement? _problem;

    [Given("que inicié sesión como médico para cerrar un bloque")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "cerrar-bloque", userId => _doctorUserId = userId);
        (_doctorId, _specialtyId) = await WithContextAsync(async context =>
        {
            var doctorId = await context.Database.SqlQuery<int>(
                $"SELECT id AS \"Value\" FROM people.doctor WHERE user_id = {_doctorUserId}").SingleAsync();
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            return (doctorId, specialtyId);
        });
    }

    [Given("que tengo un bloque que empieza dentro de {int} hora y dura {int} horas")]
    public async Task DadoQueTengoUnBloquePronto(int hoursAhead, int hours)
    {
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        _blockStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(hoursAhead);
        _blockId = await InsertBlockAsync(_blockStart, _blockStart.AddHours(hours));
    }

    [Given("que tengo un bloque dentro de {int} días de {string} a {string}")]
    public async Task DadoQueTengoUnBloque(int days, string start, string end)
    {
        _day = DateOnly.FromDateTime(scenario.Factory.Clock.UtcNow.ToOffset(TimeSpan.FromHours(-4)).DateTime).AddDays(days);
        _blockStart = Local(_day, start);
        _blockId = await InsertBlockAsync(_blockStart, Local(_day, end));
    }

    [Given("que dos pacientes tienen cita en ese bloque a las {string} y a las {string}")]
    public async Task DadoQueDosPacientesTienenCita(string first, string second)
    {
        await InsertAppointmentAsync(Local(_day, first));
        await InsertAppointmentAsync(Local(_day, second));
    }

    [Given("que un paciente tiene cita al inicio de ese bloque")]
    public Task DadoQueUnPacienteTieneCitaAlInicio() => InsertAppointmentAsync(_blockStart);

    [When("cierro ese bloque sin motivo")]
    public Task CuandoCierroSinMotivo() => CloseAsync(new { });

    [When("cierro ese bloque con el motivo {string}")]
    public Task CuandoCierroConMotivo(string reason) => CloseAsync(new { reason });

    [When("cierro ese bloque con un motivo de {int} palabras")]
    public Task CuandoCierroConMotivoDePalabras(int words) =>
        CloseAsync(new { reason = string.Join(' ', Enumerable.Repeat("emergencia", words)) });

    [When("consulto las citas afectadas por cerrar ese bloque")]
    public async Task CuandoConsultoLasCitasAfectadas()
    {
        scenario.Response?.Dispose();
        _problem = null;
        scenario.Response = await scenario.Client.GetAsync($"{BlocksUrl}/{_blockId}/affected-appointments");
    }

    [Then("el aviso indica {int} cita y la advertencia de {int} palabras")]
    public async Task EntoncesElAvisoIndica(int count, int words)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var affected = (await scenario.Response!.Content.ReadFromJsonAsync<AffectedAppointmentsResponse>())!;

        affected.Count.ShouldBe(count);
        affected.IsReasonRequired.ShouldBeTrue();
        affected.IsShortNotice.ShouldBeTrue();
        affected.MinimumShortNoticeWords.ShouldBe(words);
    }

    [Then("el bloque queda cerrado y se cancelaron {int} citas")]
    public async Task EntoncesElBloqueQuedaCerrado(int cancelled)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await scenario.Response!.Content.ReadFromJsonAsync<CloseAvailabilityBlockResponse>())
            .ShouldBe(new CloseAvailabilityBlockResponse(cancelled));
        (await IsAvailableAsync()).ShouldBeFalse();
        foreach (var appointment in _appointments)
            (await StatusOfAsync(appointment.AppointmentId)).ShouldBe("cancelled");
    }

    [Then("cada paciente recibe un correo con su cita cancelada y el motivo {string}")]
    public void EntoncesCadaPacienteRecibeUnCorreo(string reason)
    {
        var sent = scenario.Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ToList();

        sent.Select(notification => notification.Recipient.Email)
            .ShouldBe(_appointments.Select(appointment => appointment.Email), ignoreOrder: true);
        sent.ShouldAllBe(notification => notification.CancellationReason == reason);
    }

    [Then("el cierre se rechaza con el código {string}")]
    public async Task EntoncesElCierreSeRechaza(string code)
    {
        ((int)scenario.Response!.StatusCode).ShouldBe(400);
        (await ProblemAsync()).GetProperty("code").GetString().ShouldBe(code);
    }

    [Then("el bloque sigue abierto y las citas siguen activas")]
    public async Task EntoncesElBloqueSigueAbierto()
    {
        (await IsAvailableAsync()).ShouldBeTrue();
        foreach (var appointment in _appointments)
            (await StatusOfAsync(appointment.AppointmentId)).ShouldBe("pending");
        scenario.Factory.Notifications.Sent.ShouldBeEmpty();
    }

    private async Task CloseAsync(object body)
    {
        scenario.Response?.Dispose();
        _problem = null;
        scenario.Response = await scenario.Client.PostAsJsonAsync($"{BlocksUrl}/{_blockId}/close", body);
    }

    /// <summary>ProblemDetails de la última respuesta; el cuerpo de la respuesta solo se puede leer una vez.</summary>
    private async Task<JsonElement> ProblemAsync() =>
        _problem ??= await scenario.Response!.Content.ReadFromJsonAsync<JsonElement>();

    // Npgsql solo escribe timestamptz en UTC.
    private Task<int> InsertBlockAsync(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var startsAtUtc = startsAt.ToUniversalTime();
        var endsAtUtc = endsAt.ToUniversalTime();
        return WithContextAsync(async context => (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({_doctorId}, NULL, {startsAtUtc}, {endsAtUtc}, true)
            RETURNING id AS "Value"
            """).ToListAsync()).Single());
    }

    private async Task InsertAppointmentAsync(DateTimeOffset startsAt)
    {
        var email = $"cierre-paciente-{Guid.NewGuid():N}@correo.com";
        var patient = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(patient);
        var appointmentId = await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, patient.Id);
            var patientId = await context.Patients.Where(value => value.UserId == patient.Id)
                .Select(value => value.Id).SingleAsync();
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, _doctorId, _specialtyId, startsAt.ToUniversalTime(), AppointmentHours, "pending");
        });
        _appointments.Add(new PatientAppointment(email, appointmentId));
    }

    private Task<bool> IsAvailableAsync() =>
        WithContextAsync(context => context.Database.SqlQuery<bool>(
            $"SELECT is_available AS \"Value\" FROM scheduling.doctor_availability WHERE id = {_blockId}").SingleAsync());

    private Task<string> StatusOfAsync(int appointmentId) =>
        WithContextAsync(context => context.Database.SqlQuery<string>(
            $"SELECT status AS \"Value\" FROM scheduling.appointment WHERE id = {appointmentId}").SingleAsync());

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static DateTimeOffset Local(DateOnly date, string time) =>
        new(date.ToDateTime(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture)), TimeSpan.FromHours(-4));
}
