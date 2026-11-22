using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class ListInsuranceProvidersTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Url = "/api/insurance-providers";
    private const string EmptyMessage = "No existen registros de proveedores de seguros";
    private const string Password = "Secreta123";

    private readonly List<InsuranceProvider> _created = [];

    public static IEnumerable<object?[]> VisibilityCases()
    {
        UserRole?[] roles = [null, UserRole.Patient, UserRole.Doctor, UserRole.Administrator];
        string?[] states = [null, "Active", "Inactive", "All"];
        foreach (var role in roles)
        foreach (var state in states)
        {
            yield return [role, state];
        }
    }

    [Theory]
    [MemberData(nameof(VisibilityCases))]
    public async Task GET_aplica_el_estado_segun_el_JWT_real_sin_revelar_inactivos_mediante_el_conteo(
        UserRole? role, string? status)
    {
        var active = await SeedAsync("Seguros Alfa");
        var inactive = await SeedAsync("Seguros Bravo", active: false);
        var other = await SeedAsync("Seguros Charlie");
        if (role.HasValue)
        {
            await AuthorizeAsync(role.Value);
        }

        var administrator = role == UserRole.Administrator;
        InsuranceProvider[] expected = administrator ? status switch
        {
            "Inactive" => [inactive],
            "All" => [active, inactive, other],
            _ => [active, other]
        } : [active, other];

        var body = await GetAsync(status: status);

        AssertPage(body, expected, expected.Length, 1, 20, administrator);
        InsuranceProvider[] bravoMatches = administrator && status is "Inactive" or "All" ? [inactive] : [];
        AssertPage(await GetAsync("bravo", status), bravoMatches, bravoMatches.Length, 1, 20, administrator);
    }

    [Fact]
    public async Task GET_devuelve_el_contrato_completo_con_el_sitio_web_nulo_presente_cuando_no_hay_sitio()
    {
        var withWebsite = await SeedAsync("Seguros Alfa");
        var withoutWebsite = await SeedAsync("Seguros Bravo", website: null);

        var body = await GetAsync();

        AssertPage(body, [withWebsite, withoutWebsite], 2, 1, 20, false);
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        items[0].GetProperty("website").GetString().ShouldBe(InsuranceProviderMother.DefaultWebsite);
        items[1].TryGetProperty("website", out var website).ShouldBeTrue();
        website.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GET_de_un_paciente_con_el_paso_de_perfil_pendiente_responde_200_con_los_activos()
    {
        var active = await SeedAsync("Seguros Alfa");
        await SeedAsync("Seguros Bravo", active: false);
        // Los access tokens vencen según el reloj real, así que la app emite con la hora actual.
        Clock.UtcNow = DateTimeOffset.UtcNow;
        await CreateConfirmedPatientAsync("paciente@correo.com", Password);
        var login = await LoginSuccessfullyAsync("paciente@correo.com", Password);
        login.PendingStep.ShouldBe(PendingStepClaimValues.PatientProfile);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        AssertPage(await GetAsync(status: "All"), [active], 1, 1, 20, false);
    }

    [Theory]
    [InlineData("medicos")]
    [InlineData("MEDICOS")]
    [InlineData("médicos")]
    [InlineData("MÉDICOS")]
    [InlineData("dicos")]
    [InlineData("  medicos  ")]
    public async Task GET_busca_subcadenas_sin_mayusculas_ni_tildes_aunque_el_nombre_las_tenga(string search)
    {
        var expected = await SeedAsync("Seguros Médicos");
        await SeedAsync("Seguros Médicos Pediátricos", active: false);
        await SeedAsync("Seguros Universal");

        AssertPage(await GetAsync(search, "All"), [expected], 1, 1, 20, false);
    }

    [Theory]
    [InlineData("univ")]
    [InlineData("UNIV")]
    [InlineData("UNIVERSÁL")]
    [InlineData("universal")]
    [InlineData("  univ  ")]
    public async Task GET_busca_por_el_inicio_del_nombre_sin_mayusculas_ni_tildes(string search)
    {
        var expected = await SeedAsync("Seguros Universal");
        await SeedAsync("Seguros Médicos");

        AssertPage(await GetAsync(search), [expected], 1, 1, 20, false);
    }

    [Theory]
    [InlineData("clinica")]
    [InlineData("CLÍNICA")]
    public async Task GET_encuentra_nombres_sin_tilde_al_buscar_con_o_sin_tilde(string search)
    {
        var withoutAccent = await SeedAsync("Clinica Nueva");
        var withAccent = await SeedAsync("Clínica Vieja");

        AssertPage(await GetAsync(search), [withoutAccent, withAccent], 2, 1, 20, false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task GET_sin_texto_de_busqueda_conserva_todos_los_activos(string? search)
    {
        var first = await SeedAsync("Alfa Seguros");
        await SeedAsync("Bravo Seguros", active: false);
        var last = await SeedAsync("Charlie Seguros");

        AssertPage(await GetAsync(search), [first, last], 2, 1, 20, false);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    public async Task GET_trata_porcentaje_y_guion_bajo_como_texto_literal(string search)
    {
        var percent = await SeedAsync("Seguros 100%");
        await SeedAsync("Seguros 1000");
        var underscore = await SeedAsync("Seguros_A");
        await SeedAsync("Seguros xA");

        AssertPage(await GetAsync(search), [search == "%" ? percent : underscore], 1, 1, 20, false);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("signature")]
    [InlineData("expired")]
    public async Task GET_con_JWT_no_valido_responde_200_como_anonimo_y_no_concede_privilegios(string kind)
    {
        var active = await SeedAsync("Seguros Alfa");
        await SeedAsync("Seguros Bravo", active: false);
        await AuthorizeAsync(UserRole.Administrator, expired: kind == "expired");
        var token = Client.DefaultRequestHeaders.Authorization!.Parameter!;
        if (kind == "malformed")
        {
            token = "token-invalido";
        }
        else if (kind == "signature")
        {
            var parts = token.Split('.');
            var signature = Base64UrlEncoder.DecodeBytes(parts[2]);
            signature[0] ^= 0xff;
            parts[2] = Base64UrlEncoder.Encode(signature);
            token = string.Join('.', parts);
        }
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        AssertPage(await GetAsync(status: "All"), [active], 1, 1, 20, false);
    }

    [Fact]
    public async Task GET_ordena_y_pagina_con_total_anterior_al_limite_sin_duplicar_elementos()
    {
        for (var number = 25; number >= 1; number--)
        {
            await SeedAsync($"Proveedor {number:00}");
        }
        await SeedAsync("Proveedor 00", active: false);
        var ordered = _created.Where(value => value.IsActive).OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();

        AssertPage(await GetAsync(), ordered[..20], 25, 1, 20, false);
        AssertPage(await GetAsync(pageNumber: 2), ordered[20..], 25, 2, 20, false);
        AssertPage(await GetAsync(pageNumber: 3), [], 25, 3, 20, false);
        AssertPage(await GetAsync(pageNumber: 2, pageSize: 1), [ordered[1]], 25, 2, 1, false);
        AssertPage(await GetAsync(pageNumber: 2, pageSize: 7), ordered[7..14], 25, 2, 7, false);
        AssertPage(await GetAsync(pageSize: 100), ordered, 25, 1, 100, false);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("search")]
    [InlineData("inactive")]
    public async Task GET_sin_coincidencias_devuelve_200_vacio_y_mensaje(string kind)
    {
        if (kind == "search")
        {
            await SeedAsync("Seguros Universal");
        }
        else if (kind == "inactive")
        {
            await SeedAsync("Seguros Universal", active: false);
        }

        AssertPage(await GetAsync(search: kind == "search" ? "ProveedorInexistente" : null), [], 0, 1, 20, false);
    }

    [Theory]
    [InlineData("pageNumber=0", "InsuranceProvider.InvalidPageNumber")]
    [InlineData("pageNumber=-1", "InsuranceProvider.InvalidPageNumber")]
    [InlineData("pageSize=0", "InsuranceProvider.InvalidPageSize")]
    [InlineData("pageSize=-1", "InsuranceProvider.InvalidPageSize")]
    [InlineData("pageSize=101", "InsuranceProvider.InvalidPageSize")]
    [InlineData("pageNumber=2147483647&pageSize=100", "InsuranceProvider.PageOffsetTooLarge")]
    public async Task GET_rechaza_paginacion_invalida_con_400_y_codigo_de_aplicacion(string query, string code)
    {
        using var response = await Client.GetAsync($"{Url}?{query}");

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe(400);
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("pageNumber=abc", "PageNumber")]
    [InlineData("pageNumber=2147483648", "PageNumber")]
    [InlineData("pageSize=x", "PageSize")]
    [InlineData("status=Unknown", "Status")]
    [InlineData("status=999", "Status")]
    [InlineData("status=", "Status")]
    public async Task GET_rechaza_formatos_invalidos_con_400_y_errores_de_binding(string query, string field)
    {
        using var response = await Client.GetAsync($"{Url}?{query}");

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe(400);
        body.GetProperty("errors").EnumerateObject()
            .ShouldContain(error => string.Equals(error.Name, field, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<InsuranceProvider> SeedAsync(
        string name, bool active = true, string? website = InsuranceProviderMother.DefaultWebsite)
    {
        var provider = InsuranceProviderMother.Valid(name, website);
        if (!active)
        {
            provider.Deactivate().IsSuccess.ShouldBeTrue();
        }
        _created.Add(provider);
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInsuranceProviderRepository>().Add(provider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        provider.Id.ShouldBeGreaterThan(0);
        return provider;
    }

    private async Task AuthorizeAsync(UserRole role, bool expired = false)
    {
        var now = DateTimeOffset.UtcNow;
        var options = Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        try
        {
            // El validador JWT usa tiempo real, no el reloj sustituido de la aplicación.
            Clock.UtcNow = expired
                ? now.AddMinutes(-options.AccessTokenMinutes - 5).AddSeconds(-options.ClockSkewSeconds)
                : now;
            // Un correo por rol: la base se reinicia en cada prueba, así que no se repite.
            var user = UserMother.WithRole(role, email: Email.Create($"proveedores-{role}@correo.com").Value,
                passwordHash: Factory.HashPassword(Password));
            await Factory.SaveAsync(user);
            user.Id.ShouldBeGreaterThan(0);
            using var response = await LoginAsync(user.Email.Value, Password);
            await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
            var login = await response.ReadLoginAsync();
            login.RequiresTwoFactor.ShouldBeFalse();
            login.AccessToken.ShouldNotBeNullOrEmpty();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        }
        finally
        {
            Clock.UtcNow = now;
        }
    }

    private async Task<JsonElement> GetAsync(string? search = null, string? status = null, int? pageNumber = null, int? pageSize = null)
    {
        var query = new Dictionary<string, string?>();
        if (search is not null) query["search"] = search;
        if (status is not null) query["status"] = status;
        if (pageNumber.HasValue) query["pageNumber"] = pageNumber.Value.ToString(CultureInfo.InvariantCulture);
        if (pageSize.HasValue) query["pageSize"] = pageSize.Value.ToString(CultureInfo.InvariantCulture);
        using var response = await Client.GetAsync(QueryHelpers.AddQueryString(Url, query));
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static void AssertPage(
        JsonElement body, IReadOnlyList<InsuranceProvider> expected, int total, int page, int size, bool includeStatus)
    {
        body.GetProperty("pageNumber").GetInt32().ShouldBe(page);
        body.GetProperty("pageSize").GetInt32().ShouldBe(size);
        body.GetProperty("totalCount").GetInt32().ShouldBe(total);
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        items.Select(item => item.GetProperty("id").GetInt32()).ShouldBe(expected.Select(value => value.Id));
        for (var index = 0; index < items.Length; index++)
        {
            var provider = expected[index];
            items[index].GetProperty("name").GetString().ShouldBe(provider.Name);
            items[index].GetProperty("contactPhone").GetString().ShouldBe(provider.ContactPhone);
            items[index].GetProperty("contactEmail").GetString().ShouldBe(provider.ContactEmail.Value);
            items[index].GetProperty("address").GetString().ShouldBe(provider.Address);
            items[index].GetProperty("website").GetString().ShouldBe(provider.Website);
            items[index].TryGetProperty("isActive", out var active).ShouldBe(includeStatus);
            if (includeStatus) active.GetBoolean().ShouldBe(provider.IsActive);
        }
        body.TryGetProperty("message", out var message).ShouldBe(total == 0);
        if (total == 0) message.GetString().ShouldBe(EmptyMessage);
    }
}
