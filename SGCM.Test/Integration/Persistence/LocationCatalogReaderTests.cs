using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.People.Queries.ListMunicipalities;
using SGCM.Data.Application.People.Queries.ListProvinces;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class LocationCatalogReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task Listar_provincias_devuelve_una_lista_vacia_y_el_rollback_recupera_el_catalogo()
    {
        await using var db = CreateContext();
        var before = await CountCatalogAsync(db);
        before.ShouldBe(new CatalogCounts(32, 158));
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM people.municipality");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM people.province");
            (await CountCatalogAsync(db)).ShouldBe(new CatalogCounts(0, 0));

            var handler = new ListProvincesQueryHandler(new LocationCatalogReader(db));
            var result = await handler.HandleAsync(new ListProvincesQuery(), CancellationToken.None);
            result.Value.ShouldBeEmpty();
        }
        finally
        {
            (await CountCatalogAsync(db)).ShouldBe(before);
        }
    }

    [Fact]
    public async Task El_catalogo_real_conserva_los_nombres_y_se_puede_leer_en_una_transaccion_de_solo_lectura()
    {
        var capture = new CommandCapture();
        await using var db = CreateContext(capture);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        ILocationCatalogReader reader = new LocationCatalogReader(db);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        var provinces = await reader.ListProvincesAsync(token);
        provinces.Count.ShouldBe(32);
        provinces.Select(province => province.Id).Distinct().Count().ShouldBe(provinces.Count);
        provinces.ShouldContain(province => province.Name == "Samaná");
        var district = provinces.Single(province => province.Name == "Distrito Nacional");
        var municipalities = await reader.ListMunicipalitiesAsync(district.Id, token);
        municipalities.ShouldNotBeNull().ShouldHaveSingleItem().Name.ShouldBe("Santo Domingo de Guzmán");
        db.ChangeTracker.Entries().ShouldBeEmpty();

        capture.Commands.Count.ShouldBe(2);
        var provincesCommand = capture.Commands[0];
        provincesCommand.Sql.ShouldContain("ORDER BY name, id");
        provincesCommand.Parameters.ShouldBeEmpty();
        var municipalitiesCommand = capture.Commands[1];
        municipalitiesCommand.Sql.ShouldContain("WHERE p.id = @");
        municipalitiesCommand.Sql.ShouldContain("ORDER BY m.name, m.id");
        municipalitiesCommand.Parameters.ShouldBe(new object?[] { district.Id });
        foreach (var command in capture.Commands)
        {
            command.Token.ShouldBe(token);
            command.Sql.TrimStart().ShouldStartWith("SELECT");
        }
    }

    [Fact]
    public async Task Municipios_excluye_otra_provincia_aunque_los_nombres_coincidan_y_ordena_los_propios()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var province = await InsertProvinceAsync(db, "Área 233");
        var otherProvince = await InsertProvinceAsync(db, "Área 233 Norte");
        var last = await InsertMunicipalityAsync(db, province, "Zona 233 Sur");
        var first = await InsertMunicipalityAsync(db, province, "Álamo 233");
        await InsertMunicipalityAsync(db, otherProvince, "Álamo 233");
        await InsertMunicipalityAsync(db, otherProvince, "Zona 233 Norte");
        var reader = new LocationCatalogReader(db);
        var expected = await db.Database.SqlQuery<MunicipalityDto>($"""
            SELECT id, name FROM people.municipality WHERE id IN ({first}, {last}) ORDER BY name, id
            """).ToArrayAsync();

        var provinces = await reader.ListProvincesAsync(CancellationToken.None);
        provinces.ShouldContain(item => item.Id == province && item.Name == "Área 233");
        provinces.ShouldContain(item => item.Id == otherProvince && item.Name == "Área 233 Norte");
        var actual = await reader.ListMunicipalitiesAsync(province, CancellationToken.None);
        actual.ShouldNotBeNull().ShouldBe(expected);
    }

    [Fact]
    public async Task Una_provincia_vacia_devuelve_una_lista_y_una_provincia_ausente_devuelve_null()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var province = await InsertProvinceAsync(db, "Área vacía 233");
        var reader = new LocationCatalogReader(db);

        var empty = await reader.ListMunicipalitiesAsync(province, CancellationToken.None);
        empty.ShouldNotBeNull().ShouldBeEmpty();
        await transaction.RollbackAsync();
        (await reader.ListMunicipalitiesAsync(province, CancellationToken.None)).ShouldBeNull();
    }

    private AppDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Database.ConnectionString)
            .UseSnakeCaseNamingConvention().AddInterceptors(interceptors).Options);

    private static Task<CatalogCounts> CountCatalogAsync(AppDbContext db) =>
        db.Database.SqlQuery<CatalogCounts>($"""
            SELECT (SELECT count(*) FROM people.province) AS provinces,
                   (SELECT count(*) FROM people.municipality) AS municipalities
            """).SingleAsync();

    private static async Task<int> InsertProvinceAsync(AppDbContext db, string name) =>
        (await db.Database.SqlQuery<int>($"""
            INSERT INTO people.province (name) VALUES ({name}) RETURNING id AS "Value"
            """).ToListAsync()).ShouldHaveSingleItem();

    private static async Task<int> InsertMunicipalityAsync(AppDbContext db, int provinceId, string name) =>
        (await db.Database.SqlQuery<int>($"""
            INSERT INTO people.municipality (province_id, name) VALUES ({provinceId}, {name}) RETURNING id AS "Value"
            """).ToListAsync()).ShouldHaveSingleItem();

    private sealed record CatalogCounts(long Provinces, long Municipalities);
    private sealed record ObservedCommand(string Sql, object?[] Parameters, CancellationToken Token);

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<ObservedCommand> Commands { get; } = [];

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Commands.Add(new(command.CommandText,
                command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray(), cancellationToken));
            return ValueTask.FromResult(result);
        }
    }
}
