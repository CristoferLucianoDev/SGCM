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
public sealed class ReprogramacionDeBloqueSteps(ApiScenario scenario)
{
    private const string BlocksUrl = "/api/doctors/me/availability-blocks";
    private const int AppointmentHours = 2;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);

    private sealed record PatientAppointment(string Email, string Time, int AppointmentId);

    private sealed record BlockTimes(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

    private readonly List<PatientAppointment> _appointments = [];
    private int _doctorUserId;
    private int _doctorId;
    private int _specialtyId;
    private int _blockId;
    private DateOnly _today;
    private DateOnly _day;
    private JsonElement? _problem;

    [Given("que inicié sesión como médico para reprogramar un bloque")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "reprogramar-bloque", userId => _doctorUserId = userId);
        (_doctorId, _specialtyId) = await WithContextAsync(async context =>
        {
            var doctorId = await context.Database.SqlQuery<int>(
                $"SELECT id AS \"Value\" FROM people.doctor WHERE user_id = {_doctorUserId}").SingleAsync();
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            return (doctorId, specialtyId);
        });
    }

    // Va al próximo martes o jueves a las 10:00, menos de una semana por delante, para que la sesión siga activa.
    [Given("que hoy es martes")]
    public void DadoQueHoyEsMartes() => MoveClockTo(DayOfWeek.Tuesday);

    [Given("que hoy es jueves")]
    public void DadoQueHoyEsJueves() => MoveClockTo(DayOfWeek.Thursday);

    [Given("que tengo un bloque el miércoles de la semana siguiente de {string} a {string}")]
    public Task DadoQueTengoUnBloqueLaSemanaSiguiente(string start, string end) => GivenBlockAsync(9, start, end);

    [Given("que tengo un bloque el viernes de esta semana de {string} a {string}")]
    public Task DadoQueTengoUnBloqueEstaSemana(string start, string end) => GivenBlockAsync(4, start, end);

    [Given("que tengo un bloque el miércoles de dentro de dos semanas de {string} a {string}")]
    public Task DadoQueTengoUnBloqueEnDosSemanas(string start, string end) => GivenBlockAsync(16, start, end);

    [Given("que un paciente tiene cita en ese bloque a las {string} y otro a las {string}")]
    public async Task DadoQueDosPacientesTienenCita(string first, string second)
    {
        await InsertAppointmentAsync(first);
        await InsertAppointmentAsync(second);
    }

    [When("reprogramo ese bloque de {string} a {string} sin motivo")]
    public Task CuandoReprogramoSinMotivo(string start, string end) => RescheduleAsync(start, end, null);

    [When("reprogramo ese bloque de {string} a {string} con el motivo {string}")]
    public Task CuandoReprogramoConMotivo(string start, string end, string reason) => RescheduleAsync(start, end, reason);

    [Then("el bloque queda de {string} a {string} y se cancelaron {int} citas")]
    public async Task EntoncesElBloqueQueda(string start, string end, int cancelled)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await scenario.Response!.Content.ReadFromJsonAsync<RescheduleAvailabilityBlockResponse>())
            .ShouldBe(new RescheduleAvailabilityBlockResponse(cancelled));
        (await ReadBlockAsync()).ShouldBe(new BlockTimes(Local(_day, start), Local(_day, end)));
    }

    [Then("el bloque sigue de {string} a {string}")]
    public async Task EntoncesElBloqueSigue(string start, string end)
    {
        (await ReadBlockAsync()).ShouldBe(new BlockTimes(Local(_day, start), Local(_day, end)));
        foreach (var appointment in _appointments)
            (await StatusOfAsync(appointment.AppointmentId)).ShouldBe("pending");
        scenario.Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Then("la reprogramación se rechaza con el código {string}")]
    public async Task EntoncesLaReprogramacionSeRechaza(string code)
    {
        ((int)scenario.Response!.StatusCode).ShouldBe(400);
        (await ProblemAsync()).GetProperty("code").GetString().ShouldBe(code);
    }

    [Then("el mensaje indica que el bloque solo se puede cerrar")]
    public async Task EntoncesElMensajeIndicaQueSoloSePuedeCerrar() =>
        (await ProblemAsync()).GetProperty("title").GetString()!.ShouldContain("solo se puede cerrar");

    [Then("solo el paciente de las {string} tiene la cita cancelada y recibe un correo con el motivo {string}")]
    public async Task EntoncesSoloElPacienteTieneLaCitaCancelada(string time, string reason)
    {
        foreach (var appointment in _appointments)
            (await StatusOfAsync(appointment.AppointmentId)).ShouldBe(appointment.Time == time ? "cancelled" : "pending");

        var sent = scenario.Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldHaveSingleItem();
        sent.Recipient.Email.ShouldBe(_appointments.Single(appointment => appointment.Time == time).Email);
        sent.CancellationReason.ShouldBe(reason);
    }

    private void MoveClockTo(DayOfWeek dayOfWeek)
    {
        var realNow = DateTimeOffset.UtcNow;
        var day = DateOnly.FromDateTime(realNow.ToOffset(SantoDomingo).DateTime);
        while (day.DayOfWeek != dayOfWeek || Local(day, "10:00") <= realNow)
            day = day.AddDays(1);
        scenario.Factory.Clock.UtcNow = Local(day, "10:00").ToUniversalTime();
        _today = day;
    }

    private Task GivenBlockAsync(int daysFromMonday, string start, string end)
    {
        _day = _today.AddDays(-(((int)_today.DayOfWeek + 6) % 7) + daysFromMonday);
        return InsertBlockAsync(Local(_day, start), Local(_day, end));
    }

    private async Task RescheduleAsync(string start, string end, string? reason)
    {
        scenario.Response?.Dispose();
        _problem = null;
        var request = new RescheduleAvailabilityBlockRequest(_day, Time(start), Time(end), reason);
        scenario.Response = await scenario.Client.PutAsJsonAsync($"{BlocksUrl}/{_blockId}", request);
    }

    /// <summary>ProblemDetails de la última respuesta; el cuerpo de la respuesta solo se puede leer una vez.</summary>
    private async Task<JsonElement> ProblemAsync() =>
        _problem ??= await scenario.Response!.Content.ReadFromJsonAsync<JsonElement>();

    // Npgsql solo escribe timestamptz en UTC.
    private async Task InsertBlockAsync(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var startsAtUtc = startsAt.ToUniversalTime();
        var endsAtUtc = endsAt.ToUniversalTime();
        _blockId = await WithContextAsync(async context => (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({_doctorId}, NULL, {startsAtUtc}, {endsAtUtc}, true)
            RETURNING id AS "Value"
            """).ToListAsync()).Single());
    }

    private async Task InsertAppointmentAsync(string time)
    {
        var email = $"reprogramar-paciente-{Guid.NewGuid():N}@correo.com";
        var patient = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(patient);
        var appointmentId = await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, patient.Id);
            var patientId = await context.Patients.Where(value => value.UserId == patient.Id)
                .Select(value => value.Id).SingleAsync();
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, _doctorId, _specialtyId, Local(_day, time).ToUniversalTime(), AppointmentHours, "pending");
        });
        _appointments.Add(new PatientAppointment(email, time, appointmentId));
    }

    // Las fechas vuelven en UTC; la igualdad de DateTimeOffset compara el instante.
    private Task<BlockTimes> ReadBlockAsync() =>
        WithContextAsync(context => context.Database.SqlQuery<BlockTimes>(
            $"SELECT starts_at, ends_at FROM scheduling.doctor_availability WHERE id = {_blockId}").SingleAsync());

    private Task<string> StatusOfAsync(int appointmentId) =>
        WithContextAsync(context => context.Database.SqlQuery<string>(
            $"SELECT status AS \"Value\" FROM scheduling.appointment WHERE id = {appointmentId}").SingleAsync());

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static TimeOnly Time(string time) => TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture);

    private static DateTimeOffset Local(DateOnly date, string time) => new(date.ToDateTime(Time(time)), SantoDomingo);
}
