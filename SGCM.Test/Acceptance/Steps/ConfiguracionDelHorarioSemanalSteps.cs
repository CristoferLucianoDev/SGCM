using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Jobs;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class ConfiguracionDelHorarioSemanalSteps(ApiScenario scenario)
{
    private const string Url = "/api/doctors/me/availability-series";
    private const int HorizonDays = CreateAvailabilitySeriesCommandMother.BookingHorizonDays;

    private static readonly int[] EveryDay = [1, 2, 3, 4, 5, 6, 7];
    private static readonly TimeZoneInfo SantoDomingo = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    private static readonly Dictionary<string, int> IsoDayByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lunes"] = 1, ["martes"] = 2, ["miércoles"] = 3, ["jueves"] = 4,
        ["viernes"] = 5, ["sábado"] = 6, ["domingo"] = 7
    };

    private int _userId;
    private int _doctorId;
    private DateOnly _today;
    private int _blocksBeforeAdvance;

    [Given("que inicié sesión como médico para configurar mi horario")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "horario", userId => _userId = userId);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        _doctorId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Doctors
            .Where(doctor => doctor.UserId == _userId).Select(doctor => doctor.Id).SingleAsync();
        // Los tokens vencen según el reloj real, así que el reloj falso está en la hora actual.
        _today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(scenario.Factory.Clock.UtcNow, SantoDomingo).DateTime);
    }

    [Given("que creé una serie todos los días de {string} a {string} desde hoy")]
    public async Task DadoQueCreeUnaSerie(string start, string end)
    {
        await PostAsync(EveryDay, start, end, _today, null);
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [Given("que tengo un bloque suelto dentro de {int} días de {string} a {string}")]
    public async Task DadoQueTengoUnBloqueSuelto(int days, string start, string end)
    {
        var startsAt = Utc(_today.AddDays(days), Time(start));
        var endsAt = Utc(_today.AddDays(days), Time(end));
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({_doctorId}, NULL, {startsAt}, {endsAt}, true)
            """);
    }

    [When("creo una serie los días {string} de {string} a {string} desde hoy")]
    public Task CreoUnaSerieDesdeHoy(string days, string start, string end) =>
        PostAsync(Days(days), start, end, _today, null);

    [When("creo una serie los días {string} de {string} a {string} desde ayer")]
    public Task CreoUnaSerieDesdeAyer(string days, string start, string end) =>
        PostAsync(Days(days), start, end, _today.AddDays(-1), null);

    [When("creo una serie todos los días de {string} a {string} desde hoy")]
    public Task CreoUnaSerieTodosLosDias(string start, string end) =>
        PostAsync(EveryDay, start, end, _today, null);

    [When("creo una serie todos los días de {string} a {string} desde hoy hasta dentro de {int} días")]
    public Task CreoUnaSerieConFechaHasta(string start, string end, int days) =>
        PostAsync(EveryDay, start, end, _today, _today.AddDays(days));

    [When("pasa un día y se ejecuta la generación de bloques")]
    public async Task PasaUnDia()
    {
        _blocksBeforeAdvance = (await ReadBlocksAsync()).Count;
        scenario.Factory.Clock.UtcNow = scenario.Factory.Clock.UtcNow.AddDays(1);
        await scenario.Factory.GetJob<GenerateAvailabilityBlocksJob>().RunOnceAsync(CancellationToken.None);
    }

    [Then("la serie es creada")]
    public async Task LaSerieEsCreada()
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await scenario.Response!.Content.ReadFromJsonAsync<CreateAvailabilitySeriesResponse>())!.Id.ShouldBePositive();
    }

    [Then("tengo un bloque de {string} a {string} cada {string} desde hoy hasta 90 días adelante")]
    public async Task TengoUnBloquePorDia(string start, string end, string days)
    {
        var weekdays = Days(days).Select(iso => iso == 7 ? DayOfWeek.Sunday : (DayOfWeek)iso).ToHashSet();
        var expected = Enumerable.Range(0, HorizonDays + 1).Select(_today.AddDays)
            .Where(date => weekdays.Contains(date.DayOfWeek))
            .ToList();

        var blocks = await ReadBlocksAsync();

        blocks.Select(block => block.StartsAt).ShouldBe(expected.Select(date => Utc(date, Time(start))));
        blocks.Select(block => block.EndsAt).ShouldBe(expected.Select(date => Utc(date, Time(end))));
        blocks.ShouldAllBe(block => block.SeriesId != null && block.IsAvailable && !block.IsModified);
    }

    [Then("tengo {int} bloques y el último es dentro de {int} días")]
    public async Task TengoBloquesHastaLaFechaHasta(int count, int days)
    {
        var blocks = await ReadBlocksAsync();

        blocks.Count.ShouldBe(count);
        blocks[^1].StartsAt.ShouldBe(Utc(_today.AddDays(days), new TimeOnly(8, 0)));
    }

    [Then("se agregó solo el bloque del día 91")]
    public async Task SeAgregoElBloqueNuevo()
    {
        var blocks = await ReadBlocksAsync();

        _blocksBeforeAdvance.ShouldBe(HorizonDays + 1);
        blocks.Count.ShouldBe(_blocksBeforeAdvance + 1);
        blocks[^1].StartsAt.ShouldBe(Utc(_today.AddDays(HorizonDays + 1), new TimeOnly(8, 0)));
    }

    [Then("la serie es rechazada con el error {string}")]
    public async Task LaSerieEsRechazada(string errorCode)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await scenario.Response!.ReadErrorCodeAsync()).ShouldBe(errorCode);
    }

    [Then("la serie es rechazada por traslape")]
    public async Task LaSerieEsRechazadaPorTraslape()
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await scenario.Response!.ReadErrorCodeAsync()).ShouldBe(DoctorAvailabilitySeriesErrors.Overlap.Code);
    }

    [Then("no tengo series ni bloques")]
    public async Task NoTengoSeriesNiBloques()
    {
        (await ReadSeriesCountAsync()).ShouldBe(0);
        (await ReadBlocksAsync()).ShouldBeEmpty();
    }

    [Then("sigo teniendo una sola serie y sus bloques")]
    public async Task SigoTeniendoUnaSolaSerie()
    {
        (await ReadSeriesCountAsync()).ShouldBe(1);
        (await ReadBlocksAsync()).Count.ShouldBe(HorizonDays + 1);
    }

    [Then("solo tengo el bloque suelto")]
    public async Task SoloTengoElBloqueSuelto()
    {
        (await ReadSeriesCountAsync()).ShouldBe(0);
        (await ReadBlocksAsync()).ShouldHaveSingleItem().SeriesId.ShouldBeNull();
    }

    [Then("tengo dos series con sus bloques")]
    public async Task TengoDosSeries()
    {
        (await ReadSeriesCountAsync()).ShouldBe(2);
        (await ReadBlocksAsync()).Count.ShouldBe(2 * (HorizonDays + 1));
    }

    private async Task PostAsync(int[] days, string start, string end, DateOnly startDate, DateOnly? endDate)
    {
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.PostAsJsonAsync(Url,
            new CreateAvailabilitySeriesRequest(days, Time(start), Time(end), startDate, endDate));
    }

    private static int[] Days(string names) =>
        names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => IsoDayByName[name]).ToArray();

    private static TimeOnly Time(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    // Santo Domingo está en UTC-4 todo el año.
    private static DateTimeOffset Utc(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), TimeSpan.FromHours(-4)).ToUniversalTime();

    private async Task<int> ReadSeriesCountAsync()
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DoctorAvailabilitySeries
            .CountAsync(series => series.DoctorId == _doctorId);
    }

    private async Task<List<DoctorAvailability>> ReadBlocksAsync()
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DoctorAvailabilities.AsNoTracking()
            .Where(block => block.DoctorId == _doctorId).OrderBy(block => block.StartsAt).ToListAsync();
    }
}
