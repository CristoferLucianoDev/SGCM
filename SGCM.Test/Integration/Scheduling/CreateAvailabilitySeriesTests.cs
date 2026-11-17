using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Jobs;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

[Trait("Category", "Integration")]
public sealed class CreateAvailabilitySeriesTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Url = "/api/doctors/me/availability-series";
    private const string Password = "Secreta123";
    private const int HorizonDays = CreateAvailabilitySeriesCommandMother.BookingHorizonDays;

    private static readonly int[] EveryDay = [1, 2, 3, 4, 5, 6, 7];
    private static readonly int[] MondayWednesdayFriday = [1, 3, 5];
    private static readonly TimeOnly Eight = new(8, 0);
    private static readonly TimeOnly Two = new(14, 0);
    private static readonly TimeZoneInfo SantoDomingo = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    [Fact]
    public async Task El_medico_crea_su_serie_y_se_genera_un_bloque_por_dia_desde_hoy_hasta_90_dias()
    {
        var doctorId = await LoginDoctorAsync();
        var today = Today();

        using var response = await PostAsync(Request(EveryDay, today));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var seriesId = (await response.Content.ReadFromJsonAsync<CreateAvailabilitySeriesResponse>())!.Id;
        seriesId.ShouldBePositive();
        var series = (await ReadSeriesAsync(doctorId)).ShouldHaveSingleItem();
        series.Id.ShouldBe(seriesId);
        series.DaysOfWeek.Count.ShouldBe(7);
        series.StartTime.ShouldBe(Eight);
        series.EndTime.ShouldBe(Two);
        series.StartDate.ShouldBe(today);
        series.EndDate.ShouldBeNull();
        series.TimeZone.ShouldBe("America/Santo_Domingo");
        series.IsActive.ShouldBeTrue();
        var blocks = await ReadBlocksAsync(doctorId);
        blocks.Count.ShouldBe(HorizonDays + 1);
        blocks.ShouldAllBe(block => block.SeriesId == seriesId && block.IsAvailable && !block.IsModified);
        blocks.Select(block => block.StartsAt).ShouldBe(
            Enumerable.Range(0, HorizonDays + 1).Select(day => Utc(today.AddDays(day), Eight)));
        blocks.ShouldAllBe(block => block.EndsAt - block.StartsAt == TimeSpan.FromHours(6));
    }

    [Fact]
    public async Task Solo_se_generan_bloques_los_dias_elegidos()
    {
        var doctorId = await LoginDoctorAsync();
        var today = Today();

        using var response = await PostAsync(Request(MondayWednesdayFriday, today));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var expected = Enumerable.Range(0, HorizonDays + 1).Select(today.AddDays)
            .Where(date => date.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday)
            .Select(date => Utc(date, Eight));
        (await ReadBlocksAsync(doctorId)).Select(block => block.StartsAt).ShouldBe(expected);
    }

    [Fact]
    public async Task Con_fecha_hasta_los_bloques_terminan_ese_dia()
    {
        var doctorId = await LoginDoctorAsync();
        var today = Today();

        using var response = await PostAsync(Request(EveryDay, today) with { EndDate = today.AddDays(9) });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var blocks = await ReadBlocksAsync(doctorId);
        blocks.Count.ShouldBe(10);
        blocks[^1].StartsAt.ShouldBe(Utc(today.AddDays(9), Eight));
    }

    [Fact]
    public async Task Una_serie_que_empieza_mas_adelante_genera_desde_su_fecha_desde()
    {
        var doctorId = await LoginDoctorAsync();
        var today = Today();

        using var response = await PostAsync(Request(EveryDay, today.AddDays(30)));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var blocks = await ReadBlocksAsync(doctorId);
        blocks.Count.ShouldBe(HorizonDays - 30 + 1);
        blocks[0].StartsAt.ShouldBe(Utc(today.AddDays(30), Eight));
    }

    [Fact]
    public async Task Al_avanzar_un_dia_el_job_agrega_el_bloque_nuevo_y_no_duplica_los_demas()
    {
        var doctorId = await LoginDoctorAsync();
        var today = Today();
        using (var response = await PostAsync(Request(EveryDay, today)))
            await response.ShouldHaveStatusAsync(HttpStatusCode.Created);

        await RunGenerationJobAsync();
        (await ReadBlocksAsync(doctorId)).Count.ShouldBe(HorizonDays + 1);

        Clock.UtcNow = Clock.UtcNow.AddDays(1);
        await RunGenerationJobAsync();
        await RunGenerationJobAsync();

        var blocks = await ReadBlocksAsync(doctorId);
        blocks.Count.ShouldBe(HorizonDays + 2);
        blocks[^1].StartsAt.ShouldBe(Utc(today.AddDays(HorizonDays + 1), Eight));
        blocks.Select(block => block.StartsAt).Distinct().Count().ShouldBe(HorizonDays + 2);
    }

    [Fact]
    public async Task Una_serie_que_se_traslapa_con_otra_serie_activa_se_rechaza_y_no_cambia_nada()
    {
        var doctorId = await LoginDoctorAsync();
        var today = Today();
        using (var first = await PostAsync(Request(MondayWednesdayFriday, today)))
            await first.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var blocksBefore = (await ReadBlocksAsync(doctorId)).Count;

        // Mismo lunes, de 10:00 a 16:00: se cruza con el horario de 8:00 a 14:00.
        using var response = await PostAsync(
            Request([1], today) with { StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(16, 0) });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(DoctorAvailabilitySeriesErrors.Overlap.Code);
        (await ReadSeriesAsync(doctorId)).Count.ShouldBe(1);
        (await ReadBlocksAsync(doctorId)).Count.ShouldBe(blocksBefore);
    }

    [Fact]
    public async Task Una_serie_que_se_traslapa_con_un_bloque_existente_se_rechaza()
    {
        var doctorId = await LoginDoctorAsync();
        var today = Today();
        var looseStart = Utc(today.AddDays(5), new TimeOnly(9, 0));
        await WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctorId}, NULL, {looseStart}, {looseStart.AddHours(6)}, true)
            """));

        using var response = await PostAsync(Request(EveryDay, today));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(DoctorAvailabilitySeriesErrors.Overlap.Code);
        (await ReadSeriesAsync(doctorId)).ShouldBeEmpty();
        (await ReadBlocksAsync(doctorId)).ShouldHaveSingleItem().SeriesId.ShouldBeNull();
    }

    [Fact]
    public async Task Una_serie_contigua_a_otra_se_acepta()
    {
        var doctorId = await LoginDoctorAsync();
        var today = Today();
        using (var first = await PostAsync(Request(EveryDay, today)))
            await first.ShouldHaveStatusAsync(HttpStatusCode.Created);

        using var response = await PostAsync(
            Request(EveryDay, today) with { StartTime = Two, EndTime = new TimeOnly(20, 0) });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await ReadSeriesAsync(doctorId)).Count.ShouldBe(2);
        (await ReadBlocksAsync(doctorId)).Count.ShouldBe(2 * (HorizonDays + 1));
    }

    [Fact]
    public async Task El_horario_de_otro_medico_no_cuenta_como_traslape()
    {
        var otherDoctorId = await LoginDoctorAsync();
        var today = Today();
        using (var first = await PostAsync(Request(EveryDay, today)))
            await first.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var doctorId = await LoginDoctorAsync();

        using var response = await PostAsync(Request(EveryDay, today));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await ReadBlocksAsync(doctorId)).Count.ShouldBe(HorizonDays + 1);
        (await ReadBlocksAsync(otherDoctorId)).Count.ShouldBe(HorizonDays + 1);
    }

    [Fact]
    public async Task Un_horario_de_exactamente_6_horas_se_acepta()
    {
        await LoginDoctorAsync();

        using var response = await PostAsync(Request(EveryDay, Today()) with { EndTime = Two });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    public static TheoryData<string, string> InvalidRequests => new()
    {
        { "hora_no_en_punto", DoctorAvailabilitySeriesErrors.TimeNotOnHour.Code },
        { "menos_de_6_horas", DoctorAvailabilitySeriesErrors.DurationTooShort.Code },
        { "fin_antes_del_inicio", DoctorAvailabilitySeriesErrors.InvalidTimeRange.Code },
        { "sin_dias", DoctorAvailabilitySeriesErrors.DaysRequired.Code },
        { "dia_invalido", DoctorAvailabilitySeriesErrors.InvalidDayOfWeek.Code },
        { "fecha_hasta_antes_de_desde", DoctorAvailabilitySeriesErrors.InvalidDateRange.Code },
        { "fecha_desde_pasada", DoctorAvailabilitySeriesErrors.StartDateInPast.Code }
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Un_dato_invalido_responde_400_y_no_guarda_nada(string caso, string errorCode)
    {
        var doctorId = await LoginDoctorAsync();
        var valid = Request(EveryDay, Today());
        var request = caso switch
        {
            "hora_no_en_punto" => valid with { StartTime = new TimeOnly(8, 30) },
            "menos_de_6_horas" => valid with { EndTime = new TimeOnly(13, 0) },
            "fin_antes_del_inicio" => valid with { StartTime = Two, EndTime = Eight },
            "sin_dias" => valid with { DaysOfWeek = [] },
            "dia_invalido" => valid with { DaysOfWeek = [1, 8] },
            "fecha_hasta_antes_de_desde" => valid with { StartDate = Today().AddDays(5), EndDate = Today().AddDays(4) },
            "fecha_desde_pasada" => valid with { StartDate = Today().AddDays(-1) },
            _ => throw new ArgumentOutOfRangeException(nameof(caso), caso, "Caso de prueba desconocido.")
        };

        using var response = await PostAsync(request);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(errorCode);
        (await ReadSeriesAsync(doctorId)).ShouldBeEmpty();
        (await ReadBlocksAsync(doctorId)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Administrator)]
    public async Task Otros_roles_reciben_403(UserRole role)
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var email = $"rol-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(role, email, Password, mustChangePassword: false);
        await WithContextAsync(async context => await TestDataSeeder.InsertProfileAsync(context, (await FindUserAsync(email))!));
        Authorize((await LoginSuccessfullyAsync(email, Password)).AccessToken);

        using var response = await PostAsync(Request(EveryDay, Today()));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_medico_con_el_perfil_pendiente_recibe_403()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var email = $"pendiente-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(UserRole.Doctor, email, Password, mustChangePassword: false);
        var login = await LoginSuccessfullyAsync(email, Password);
        login.PendingStep.ShouldNotBeNull();
        Authorize(login.AccessToken);

        using var response = await PostAsync(Request(EveryDay, Today()));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        using var response = await PostAsync(Request(EveryDay, DateOnly.FromDateTime(DateTime.UtcNow)));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    // Médico con perfil completo y sesión iniciada. Los tokens vencen según el reloj real,
    // así que el reloj falso arranca en la hora actual y las fechas se calculan desde hoy.
    private async Task<int> LoginDoctorAsync()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var email = $"medico-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(UserRole.Doctor, email, Password, mustChangePassword: false);
        var user = (await FindUserAsync(email))!;
        var doctorId = 0;
        await WithContextAsync(async context =>
            doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id));

        var login = await LoginSuccessfullyAsync(email, Password);
        login.PendingStep.ShouldBeNull();
        Authorize(login.AccessToken);
        return doctorId;
    }

    private static CreateAvailabilitySeriesRequest Request(int[] days, DateOnly startDate) =>
        new(days, Eight, Two, startDate, null);

    // Hoy en Santo Domingo según el reloj falso de la aplicación.
    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Clock.UtcNow, SantoDomingo).DateTime);

    // Santo Domingo está en UTC-4 todo el año.
    private static DateTimeOffset Utc(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), TimeSpan.FromHours(-4)).ToUniversalTime();

    private void Authorize(string? accessToken) =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private Task<HttpResponseMessage> PostAsync(CreateAvailabilitySeriesRequest request) =>
        Client.PostAsJsonAsync(Url, request);

    private Task RunGenerationJobAsync() =>
        Factory.GetJob<GenerateAvailabilityBlocksJob>().RunOnceAsync(CancellationToken.None);

    private async Task WithContextAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<List<DoctorAvailabilitySeries>> ReadSeriesAsync(int doctorId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DoctorAvailabilitySeries.AsNoTracking()
            .Where(series => series.DoctorId == doctorId).OrderBy(series => series.Id).ToListAsync();
    }

    private async Task<List<DoctorAvailability>> ReadBlocksAsync(int doctorId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DoctorAvailabilities.AsNoTracking()
            .Where(block => block.DoctorId == doctorId).OrderBy(block => block.StartsAt).ToListAsync();
    }
}
