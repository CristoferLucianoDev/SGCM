using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class DoctorAvailabilityRepositoryTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const int MinimumHours = DoctorAvailabilitySeriesMother.MinimumDurationHours;

    // Lunes 4 de enero de 2027, de 8:00 a 14:00 en Santo Domingo (UTC-4).
    private static readonly DateTimeOffset Start = new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task La_serie_se_guarda_y_se_lee_completa_con_los_dias_como_arreglo_ISO()
    {
        await using var provider = CreateProvider();
        var doctorId = await InsertDoctorAsync(provider);
        var endDate = new DateOnly(2027, 6, 30);
        var id = await SaveSeriesAsync(provider, DoctorAvailabilitySeriesMother.Create(
            [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Friday], endDate: endDate, doctorId: doctorId).Value);

        await using var scope = provider.CreateAsyncScope();
        var series = await scope.ServiceProvider.GetRequiredService<IDoctorAvailabilitySeriesRepository>()
            .GetByIdAsync(id, CancellationToken.None);

        series.ShouldNotBeNull();
        series.DoctorId.ShouldBe(doctorId);
        series.DaysOfWeek.ShouldBe(new[] { DayOfWeek.Monday, DayOfWeek.Friday, DayOfWeek.Sunday });
        series.StartTime.ShouldBe(DoctorAvailabilitySeriesMother.StartTime);
        series.EndTime.ShouldBe(DoctorAvailabilitySeriesMother.EndTime);
        series.StartDate.ShouldBe(DoctorAvailabilitySeriesMother.StartDate);
        series.EndDate.ShouldBe(endDate);
        series.TimeZone.ShouldBe(DoctorAvailabilitySeriesMother.TimeZone);
        series.IsActive.ShouldBeTrue();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Database.SqlQuery<string>(
                $"SELECT days_of_week::text AS \"Value\" FROM scheduling.doctor_availability_series WHERE id = {id}")
            .SingleAsync()).ShouldBe("{1,5,7}");
    }

    [Fact]
    public async Task Una_serie_sin_fecha_hasta_se_guarda_sin_fin()
    {
        await using var provider = CreateProvider();
        var id = await SaveSeriesAsync(provider,
            DoctorAvailabilitySeriesMother.Create(doctorId: await InsertDoctorAsync(provider)).Value);

        await using var scope = provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().DoctorAvailabilitySeries.AsNoTracking()
            .SingleAsync(series => series.Id == id)).EndDate.ShouldBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_devuelve_la_serie_rastreada_y_UnitOfWork_guarda_sus_cambios()
    {
        await using var provider = CreateProvider();
        var id = await SaveSeriesAsync(provider,
            DoctorAvailabilitySeriesMother.Create(doctorId: await InsertDoctorAsync(provider)).Value);

        await using (var scope = provider.CreateAsyncScope())
        {
            var series = await scope.ServiceProvider.GetRequiredService<IDoctorAvailabilitySeriesRepository>()
                .GetByIdAsync(id, CancellationToken.None);
            series!.Deactivate().IsSuccess.ShouldBeTrue();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await using var check = provider.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<AppDbContext>().DoctorAvailabilitySeries.AsNoTracking()
            .SingleAsync(series => series.Id == id)).IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_con_id_inexistente_devuelve_null()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<IDoctorAvailabilitySeriesRepository>()
            .GetByIdAsync(int.MaxValue, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Los_bloques_de_la_serie_se_guardan_con_su_serie_en_UTC_disponibles_y_sin_marca()
    {
        await using var provider = CreateProvider();
        var doctorId = await InsertDoctorAsync(provider);
        var series = DoctorAvailabilitySeriesMother.Create(doctorId: doctorId).Value;
        await SaveSeriesAsync(provider, series);
        var blocks = series.GetOccurrences(series.StartDate, series.StartDate.AddDays(6))
            .Select(occurrence => DoctorAvailability.CreateFromSeries(series, occurrence, MinimumHours).Value)
            .ToList();

        await SaveBlocksAsync(provider, blocks);

        var saved = await ReadBlocksAsync(provider, doctorId);
        saved.Count.ShouldBe(3);
        saved.ShouldAllBe(block => block.SeriesId == series.Id && block.IsAvailable && !block.IsModified);
        saved[0].StartsAt.ShouldBe(Start);
        saved[0].StartsAt.Offset.ShouldBe(TimeSpan.Zero);
        saved[0].EndsAt.ShouldBe(Start.AddHours(6));
        saved.Select(block => block.StartsAt).ShouldBe(new[] { Start, Start.AddDays(2), Start.AddDays(4) });
    }

    [Fact]
    public async Task Un_bloque_suelto_se_guarda_sin_serie()
    {
        await using var provider = CreateProvider();
        var doctorId = await InsertDoctorAsync(provider);

        await SaveBlocksAsync(provider, [Block(doctorId, Start)]);

        var saved = (await ReadBlocksAsync(provider, doctorId)).ShouldHaveSingleItem();
        saved.SeriesId.ShouldBeNull();
        saved.IsAvailable.ShouldBeTrue();
        saved.IsModified.ShouldBeFalse();
    }

    [Fact]
    public async Task Un_bloque_que_se_traslapa_con_otro_del_mismo_medico_es_un_conflicto_y_no_se_guarda()
    {
        await using var provider = CreateProvider();
        var doctorId = await InsertDoctorAsync(provider);
        await SaveBlocksAsync(provider, [Block(doctorId, Start)]);

        var conflict = await Should.ThrowAsync<PersistenceConflictException>(
            () => SaveBlocksAsync(provider, [Block(doctorId, Start.AddHours(2))]));

        conflict.Error.ShouldBe(DoctorAvailabilityErrors.Overlap);
        (await ReadBlocksAsync(provider, doctorId)).ShouldHaveSingleItem().StartsAt.ShouldBe(Start);
    }

    [Fact]
    public async Task Dos_bloques_del_mismo_lote_que_se_traslapan_no_guardan_ninguno()
    {
        await using var provider = CreateProvider();
        var doctorId = await InsertDoctorAsync(provider);

        var conflict = await Should.ThrowAsync<PersistenceConflictException>(
            () => SaveBlocksAsync(provider, [Block(doctorId, Start), Block(doctorId, Start.AddHours(5))]));

        conflict.Error.ShouldBe(DoctorAvailabilityErrors.Overlap);
        (await ReadBlocksAsync(provider, doctorId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_bloque_cerrado_tambien_impide_el_traslape()
    {
        await using var provider = CreateProvider();
        var doctorId = await InsertDoctorAsync(provider);
        await SaveBlocksAsync(provider, [Block(doctorId, Start)]);
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE scheduling.doctor_availability SET is_available = false WHERE doctor_id = {doctorId}");
        }

        var conflict = await Should.ThrowAsync<PersistenceConflictException>(
            () => SaveBlocksAsync(provider, [Block(doctorId, Start.AddHours(3))]));

        conflict.Error.ShouldBe(DoctorAvailabilityErrors.Overlap);
    }

    [Fact]
    public async Task Bloques_contiguos_del_mismo_medico_se_aceptan()
    {
        await using var provider = CreateProvider();
        var doctorId = await InsertDoctorAsync(provider);

        await SaveBlocksAsync(provider, [Block(doctorId, Start), Block(doctorId, Start.AddHours(6))]);

        (await ReadBlocksAsync(provider, doctorId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Otro_medico_puede_tener_un_bloque_a_la_misma_hora()
    {
        await using var provider = CreateProvider();
        var doctorId = await InsertDoctorAsync(provider);
        var otherDoctorId = await InsertDoctorAsync(provider);

        await SaveBlocksAsync(provider, [Block(doctorId, Start), Block(otherDoctorId, Start)]);

        (await ReadBlocksAsync(provider, doctorId)).ShouldHaveSingleItem();
        (await ReadBlocksAsync(provider, otherDoctorId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ListActiveByDoctorAsync_devuelve_solo_las_series_activas_del_medico()
    {
        await using var provider = CreateProvider();
        var doctorId = await InsertDoctorAsync(provider);
        var otherDoctorId = await InsertDoctorAsync(provider);
        var activeId = await SaveSeriesAsync(provider, DoctorAvailabilitySeriesMother.Create(doctorId: doctorId).Value);
        var inactive = DoctorAvailabilitySeriesMother.Create([DayOfWeek.Tuesday], doctorId: doctorId).Value;
        inactive.Deactivate().IsSuccess.ShouldBeTrue();
        await SaveSeriesAsync(provider, inactive);
        await SaveSeriesAsync(provider, DoctorAvailabilitySeriesMother.Create(doctorId: otherDoctorId).Value);

        await using var scope = provider.CreateAsyncScope();
        var series = await scope.ServiceProvider.GetRequiredService<IDoctorAvailabilitySeriesRepository>()
            .ListActiveByDoctorAsync(doctorId, CancellationToken.None);

        var active = series.ShouldHaveSingleItem();
        active.Id.ShouldBe(activeId);
        active.DaysOfWeek.ShouldBe(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday });
    }

    [Fact]
    public async Task ListByDoctorAsync_devuelve_los_bloques_del_medico_que_tocan_el_rango_abiertos_o_cerrados()
    {
        await using var provider = CreateProvider();
        var doctorId = await InsertDoctorAsync(provider);
        var otherDoctorId = await InsertDoctorAsync(provider);
        await SaveBlocksAsync(provider,
        [
            Block(doctorId, Start.AddDays(-2)),
            Block(doctorId, Start),
            Block(doctorId, Start.AddDays(2)),
            Block(doctorId, Start.AddDays(10)),
            Block(otherDoctorId, Start)
        ]);
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE scheduling.doctor_availability SET is_available = false WHERE doctor_id = {doctorId} AND starts_at = {Start.AddDays(2)}");
        }

        await using var check = provider.CreateAsyncScope();
        var repository = check.ServiceProvider.GetRequiredService<IDoctorAvailabilityRepository>();
        // Desde la mitad del primer bloque del rango hasta el inicio del bloque del día 10, que queda fuera.
        var bounded = await repository.ListByDoctorAsync(doctorId, Start.AddHours(3), Start.AddDays(10), CancellationToken.None);
        var unbounded = await repository.ListByDoctorAsync(doctorId, Start.AddHours(3), null, CancellationToken.None);

        bounded.Select(block => block.StartsAt).ShouldBe(new[] { Start, Start.AddDays(2) });
        bounded.ShouldContain(block => !block.IsAvailable);
        unbounded.Select(block => block.StartsAt).ShouldBe(new[] { Start, Start.AddDays(2), Start.AddDays(10) });
    }

    private static DoctorAvailability Block(int doctorId, DateTimeOffset startsAt) =>
        DoctorAvailability.Create(doctorId, null, startsAt, startsAt.AddHours(MinimumHours), MinimumHours).Value;

    private static async Task<int> InsertDoctorAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"doctor-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
    }

    private static async Task<int> SaveSeriesAsync(ServiceProvider provider, DoctorAvailabilitySeries series)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IDoctorAvailabilitySeriesRepository>().Add(series);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return series.Id;
    }

    private static async Task SaveBlocksAsync(ServiceProvider provider, IReadOnlyList<DoctorAvailability> blocks)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IDoctorAvailabilityRepository>().AddRange(blocks);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<List<DoctorAvailability>> ReadBlocksAsync(ServiceProvider provider, int doctorId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DoctorAvailabilities.AsNoTracking()
            .Where(block => block.DoctorId == doctorId)
            .OrderBy(block => block.StartsAt)
            .ToListAsync();
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options => options.SgcmDatabase = Database.ConnectionString);
        services.AddInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
