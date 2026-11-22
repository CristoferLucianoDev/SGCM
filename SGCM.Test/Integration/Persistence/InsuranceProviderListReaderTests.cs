using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.ListInsuranceProviders;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class InsuranceProviderListReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const string Phone = "8095550200";
    private const string Email = "contacto@seguros.com";
    private const string Address = "Av. Winston Churchill 100";

    [Theory]
    [InlineData("univ", new[] { "Seguros Universal" })]
    [InlineData("UNIV", new[] { "Seguros Universal" })]
    [InlineData("  UniVersal  ", new[] { "Seguros Universal" })]
    [InlineData("segur", new[] { "Seguros Médicos Ñandú", "Seguros Universal" })]
    [InlineData("medicos", new[] { "Seguros Médicos Ñandú" })]
    [InlineData("MÉDICOS", new[] { "Seguros Médicos Ñandú" })]
    [InlineData("nandu", new[] { "Seguros Médicos Ñandú" })]
    [InlineData("zzz", new string[0])]
    public async Task La_busqueda_es_parcial_y_no_distingue_mayusculas_ni_tildes(string search, string[] expected)
    {
        await InsertAsync("Seguros Universal");
        await InsertAsync("Seguros Médicos Ñandú");
        await InsertAsync("Protección Total");

        var page = await ListAsync(search: search);

        page.Items.Select(item => item.Name).ShouldBe(expected);
        page.TotalCount.ShouldBe(expected.Length);
    }

    [Fact]
    public async Task Los_metacaracteres_de_LIKE_en_la_busqueda_se_tratan_como_texto_literal()
    {
        await InsertAsync("Seguros 100% Salud");
        await InsertAsync("Seguros_Prime");
        await InsertAsync("Seguros XPrime");
        await InsertAsync("Otro Proveedor");

        (await ListAsync(search: "%")).Items.Select(item => item.Name).ShouldBe(new[] { "Seguros 100% Salud" });
        (await ListAsync(search: "_")).Items.Select(item => item.Name).ShouldBe(new[] { "Seguros_Prime" });
        // Como comodín, "s_x" encontraría "Seguros XPrime" ("s", espacio, "x"); como texto literal no aparece en ningún nombre.
        (await ListAsync(search: "s_x")).Items.ShouldBeEmpty();
        (await ListAsync(search: "100%")).Items.Select(item => item.Name).ShouldBe(new[] { "Seguros 100% Salud" });
    }

    [Theory]
    [InlineData(true, new[] { "Alfa Activo", "Charlie Activo" })]
    [InlineData(false, new[] { "Bravo Inactivo" })]
    [InlineData(null, new[] { "Alfa Activo", "Bravo Inactivo", "Charlie Activo" })]
    public async Task El_filtro_de_estado_devuelve_activos_inactivos_o_todos(bool? isActive, string[] expected)
    {
        await InsertAsync("Charlie Activo");
        await InsertAsync("Bravo Inactivo", isActive: false);
        await InsertAsync("Alfa Activo");

        var page = await ListAsync(isActive: isActive);

        page.Items.Select(item => item.Name).ShouldBe(expected);
        page.TotalCount.ShouldBe(expected.Length);
    }

    [Fact]
    public async Task IncludeStatus_informa_el_estado_de_cada_proveedor_y_sin_el_queda_en_null()
    {
        await InsertAsync("Alfa Activo");
        await InsertAsync("Bravo Inactivo", isActive: false);

        var withStatus = await ListAsync(isActive: null, includeStatus: true);
        var withoutStatus = await ListAsync(isActive: null, includeStatus: false);

        withStatus.Items.Select(item => item.IsActive).ShouldBe(new bool?[] { true, false });
        withoutStatus.Items.Select(item => item.IsActive).ShouldBe(new bool?[] { null, null });
    }

    [Fact]
    public async Task La_proyeccion_devuelve_todos_los_datos_y_el_sitio_web_nulo_llega_como_null()
    {
        var withWebsite = await InsertAsync("Alfa Seguros", website: "https://alfa.example");
        var withoutWebsite = await InsertAsync("Bravo Seguros");

        var page = await ListAsync();

        page.Items.ShouldBe(new[]
        {
            new InsuranceProviderDto(withWebsite, "Alfa Seguros", Phone, Email, Address, "https://alfa.example", null),
            new InsuranceProviderDto(withoutWebsite, "Bravo Seguros", Phone, Email, Address, null, null)
        });
    }

    [Fact]
    public async Task El_orden_es_por_nombre_sin_importar_el_orden_de_insercion()
    {
        // El nombre es único, así que el desempate por id no se puede ejercitar con datos reales; el SQL
        // generado lo verifica la prueba unitaria del lector (ORDER BY i.name, i.id).
        await InsertAsync("Charlie Seguros");
        await InsertAsync("Alfa Seguros");
        await InsertAsync("Bravo Seguros");

        var page = await ListAsync();

        page.Items.Select(item => item.Name).ShouldBe(new[] { "Alfa Seguros", "Bravo Seguros", "Charlie Seguros" });
    }

    [Fact]
    public async Task La_paginacion_cuenta_el_total_antes_de_paginar_y_devuelve_la_pagina_pedida()
    {
        foreach (var name in new[] { "E Seguros", "C Seguros", "A Seguros", "D Seguros", "B Seguros" })
            await InsertAsync(name);

        var first = await ListAsync(pageNumber: 1, pageSize: 2);
        var second = await ListAsync(pageNumber: 2, pageSize: 2);
        var last = await ListAsync(pageNumber: 3, pageSize: 2);

        first.Items.Select(item => item.Name).ShouldBe(new[] { "A Seguros", "B Seguros" });
        second.Items.Select(item => item.Name).ShouldBe(new[] { "C Seguros", "D Seguros" });
        last.Items.Select(item => item.Name).ShouldBe(new[] { "E Seguros" });
        new[] { first, second, last }.ShouldAllBe(page => page.TotalCount == 5 && page.PageSize == 2);
        second.PageNumber.ShouldBe(2);
    }

    [Fact]
    public async Task Una_pagina_fuera_de_rango_devuelve_items_vacios_y_conserva_el_total()
    {
        await InsertAsync("Alfa Seguros");
        await InsertAsync("Bravo Seguros");

        var page = await ListAsync(pageNumber: 5, pageSize: 20);

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(2);
        page.PageNumber.ShouldBe(5);
    }

    [Fact]
    public async Task La_busqueda_y_el_estado_se_combinan_con_AND_y_el_total_los_respeta()
    {
        await InsertAsync("Seguros Alfa");
        await InsertAsync("Seguros Bravo", isActive: false);
        await InsertAsync("Otro Charlie");

        var page = await ListAsync(search: "seguros", isActive: true, pageSize: 1);

        page.Items.Select(item => item.Name).ShouldBe(new[] { "Seguros Alfa" });
        page.TotalCount.ShouldBe(1);
    }

    // Inserta con SQL para controlar el estado y el sitio web sin pasar por el dominio.
    private async Task<int> InsertAsync(string name, bool isActive = true, string? website = null)
    {
        await using var context = CreateContext();
        return (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO people.insurance_provider (name, contact_phone, contact_email, address, website, is_active)
            VALUES ({name}, {Phone}, {Email}, {Address}, {website}, {isActive})
            RETURNING id AS "Value"
            """).ToListAsync()).Single();
    }

    private async Task<PagedResult<InsuranceProviderDto>> ListAsync(
        string? search = null, bool? isActive = true, bool includeStatus = false, int pageNumber = 1, int pageSize = 20)
    {
        await using var context = CreateContext();
        return await new InsuranceProviderListReader(context).ListAsync(
            search, isActive, includeStatus, pageNumber, pageSize, CancellationToken.None);
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);
}
