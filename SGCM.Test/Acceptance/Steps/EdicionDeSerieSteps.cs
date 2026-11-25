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

/// <summary>
/// Pasos de la edición de una serie. El día lo fija el paso compartido "que hoy es martes" con el reloj falso; los días
/// se cuentan desde el lunes de la semana actual, así que 7 es el lunes siguiente.
/// </summary>
[Binding]
public sealed class EdicionDeSerieSteps(ApiScenario scenario)
{
    private const string SeriesUrl = "/api/doctors/me/availability-series";
    private const string BlocksUrl = "/api/doctors/me/availability-blocks";
    private const int AppointmentHours = 2;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);

    private sealed record BlockRow(int Id, int? SeriesId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool IsAvailable, bool IsModified);

    private int _doctorUserId;
    private int _doctorId;
    private int _specialtyId;
    private int _seriesId;
    private string? _patientEmail;
    private int _appointmentId;
    private JsonElement? _problem;

    [Given("que inicié sesión como médico para editar una serie")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "editar-serie", userId => _doctorUserId = userId);
        (_doctorId, _specialtyId) = await WithContextAsync(async context =>
        {
            var doctorId = await context.Database.SqlQuery<int>(
                $"SELECT id AS \"Value\" FROM people.doctor WHERE user_id = {_doctorUserId}").SingleAsync();
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            return (doctorId, specialtyId);
        });
    }

    [Given("que tengo una serie los lunes, martes y jueves de {string} a {string} desde hoy")]
    public async Task DadoQueTengoUnaSerie(string start, string end)
    {
        using var response = await scenario.Client.PostAsJsonAsync(SeriesUrl, new CreateAvailabilitySeriesRequest(
            [1, 2, 4], Time(start), Time(end), Today(), null));
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        _seriesId = (await response.Content.ReadFromJsonAsync<CreateAvailabilitySeriesResponse>())!.Id;
    }

    [Given("que edité a mano el martes de la semana siguiente de {string} a {string}")]
    public async Task DadoQueEditeAManoElMartes(string start, string end)
    {
        var tuesday = BlockOn(await ReadBlocksAsync(), Day(8));
        using var response = await scenario.Client.PutAsJsonAsync(
            $"{BlocksUrl}/{tuesday.Id}", new RescheduleAvailabilityBlockRequest(Day(8), Time(start), Time(end)));
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
    }

    [Given("que un paciente tiene cita el jueves de la semana siguiente a las {string}")]
    public async Task DadoQueUnPacienteTieneCita(string time)
    {
        _patientEmail = $"serie-paciente-{Guid.NewGuid():N}@correo.com";
        var patient = UserMother.WithRole(UserRole.Patient, email: Email.Create(_patientEmail).Value);
        await scenario.Factory.SaveAsync(patient);
        _appointmentId = await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, patient.Id);
            var patientId = await context.Patients.Where(value => value.UserId == patient.Id)
                .Select(value => value.Id).SingleAsync();
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, _doctorId, _specialtyId, Local(Day(10), time).ToUniversalTime(), AppointmentHours, "pending");
        });
    }

    [When("edito la serie a los lunes, martes y miércoles de {string} a {string} sin motivo")]
    public Task CuandoEditoLaSerieSinMotivo(string start, string end) => UpdateAsync(start, end, null);

    [When("edito la serie a los lunes, martes y miércoles de {string} a {string} con el motivo {string}")]
    public Task CuandoEditoLaSerieConMotivo(string start, string end, string reason) => UpdateAsync(start, end, reason);

    [When("consulto mis bloques de {int} días desde el lunes siguiente")]
    public async Task CuandoConsultoMisBloques(int days)
    {
        scenario.Response?.Dispose();
        _problem = null;
        scenario.Response = await scenario.Client.GetAsync(
            $"{BlocksUrl}?from={Day(7):yyyy-MM-dd}&to={Day(7 + days - 1):yyyy-MM-dd}");
    }

    [Then("la serie cambia desde el lunes siguiente y se cancelaron {int} citas")]
    public async Task EntoncesLaSerieCambia(int cancelled)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await scenario.Response!.Content.ReadFromJsonAsync<UpdateAvailabilitySeriesResponse>())
            .ShouldBe(new UpdateAvailabilitySeriesResponse(Day(7), cancelled));
    }

    [Then("el bloque de dentro de {int} días sigue de {string} a {string}")]
    public async Task EntoncesElBloqueDeDentroDeDiasSigue(int days, string start, string end) =>
        AssertBlock(await ReadBlocksAsync(), Today().AddDays(days), start, end);

    [Then("la semana siguiente tiene bloques solo el lunes, martes y miércoles de {string} a {string}")]
    public async Task EntoncesLaSemanaSiguienteTieneBloques(string start, string end)
    {
        var nextWeek = (await ReadBlocksAsync())
            .Where(block => LocalDate(block.StartsAt) >= Day(7) && LocalDate(block.StartsAt) <= Day(13)).ToList();

        nextWeek.Select(block => LocalDate(block.StartsAt)).ShouldBe(new[] { Day(7), Day(8), Day(9) });
        foreach (var block in nextWeek)
            AssertBlock(nextWeek, LocalDate(block.StartsAt), start, end);
    }

    [Then("el martes de la semana siguiente sigue de {string} a {string}")]
    public async Task EntoncesElMartesSigue(string start, string end)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var blocks = await ReadBlocksAsync();
        AssertBlock(blocks, Day(8), start, end);
        BlockOn(blocks, Day(8)).IsModified.ShouldBeTrue();
    }

    [Then("el paciente de la serie recibe un correo con su cita cancelada y el motivo {string}")]
    public async Task EntoncesElPacienteRecibeUnCorreo(string reason)
    {
        (await StatusOfAsync(_appointmentId)).ShouldBe("cancelled");
        var sent = scenario.Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldHaveSingleItem();
        sent.Recipient.Email.ShouldBe(_patientEmail!);
        sent.CancellationReason.ShouldBe(reason);
    }

    [Then("la edición de la serie se rechaza con el código {string}")]
    public Task EntoncesLaEdicionSeRechaza(string code) => AssertRejectedAsync(code);

    [Then("la consulta de bloques se rechaza con el código {string}")]
    public Task EntoncesLaConsultaSeRechaza(string code) => AssertRejectedAsync(code);

    [Then("la cita del paciente de la serie sigue activa")]
    public async Task EntoncesLaCitaSigueActiva()
    {
        (await StatusOfAsync(_appointmentId)).ShouldBe("pending");
        AssertBlock(await ReadBlocksAsync(), Day(10), "08:00", "14:00");
        scenario.Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Then("veo mis bloques del lunes, martes y jueves de esa semana")]
    public async Task EntoncesVeoMisBloques()
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var items = (await scenario.Response!.Content.ReadFromJsonAsync<AvailabilityBlocksResponse>())!.Items;

        items.Select(block => LocalDate(block.StartsAt)).ShouldBe(new[] { Day(7), Day(8), Day(10) });
        items.ShouldAllBe(block => block.SeriesId == _seriesId && block.IsAvailable && !block.IsModified);
    }

    private async Task UpdateAsync(string start, string end, string? reason)
    {
        scenario.Response?.Dispose();
        _problem = null;
        scenario.Response = await scenario.Client.PutAsJsonAsync(
            $"{SeriesUrl}/{_seriesId}", new UpdateAvailabilitySeriesRequest([1, 2, 3], Time(start), Time(end), null, reason));
    }

    private async Task AssertRejectedAsync(string code)
    {
        ((int)scenario.Response!.StatusCode).ShouldBe(400);
        (await ProblemAsync()).GetProperty("code").GetString().ShouldBe(code);
    }

    /// <summary>ProblemDetails de la última respuesta; el cuerpo de la respuesta solo se puede leer una vez.</summary>
    private async Task<JsonElement> ProblemAsync() =>
        _problem ??= await scenario.Response!.Content.ReadFromJsonAsync<JsonElement>();

    private DateOnly Today() => LocalDate(scenario.Factory.Clock.UtcNow);

    private DateOnly Day(int daysFromMonday)
    {
        var today = Today();
        return today.AddDays(-(((int)today.DayOfWeek + 6) % 7) + daysFromMonday);
    }

    private static void AssertBlock(IEnumerable<BlockRow> blocks, DateOnly day, string start, string end)
    {
        var block = BlockOn(blocks, day);
        block.StartsAt.ShouldBe(Local(day, start));
        block.EndsAt.ShouldBe(Local(day, end));
        block.IsAvailable.ShouldBeTrue();
    }

    private static BlockRow BlockOn(IEnumerable<BlockRow> blocks, DateOnly day) =>
        blocks.Single(block => LocalDate(block.StartsAt) == day);

    // Las fechas vuelven en UTC; la igualdad de DateTimeOffset compara el instante.
    private Task<List<BlockRow>> ReadBlocksAsync() =>
        WithContextAsync(context => context.Database.SqlQuery<BlockRow>(
            $"""
            SELECT id, series_id, starts_at, ends_at, is_available, is_modified
            FROM scheduling.doctor_availability
            WHERE doctor_id = {_doctorId}
            ORDER BY starts_at
            """).ToListAsync());

    private Task<string> StatusOfAsync(int appointmentId) =>
        WithContextAsync(context => context.Database.SqlQuery<string>(
            $"SELECT status AS \"Value\" FROM scheduling.appointment WHERE id = {appointmentId}").SingleAsync());

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(SantoDomingo).DateTime);

    private static TimeOnly Time(string time) => TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture);

    private static DateTimeOffset Local(DateOnly date, string time) => new(date.ToDateTime(Time(time)), SantoDomingo);
}
