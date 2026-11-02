using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class ListSpecialtiesTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Url = "/api/specialties";
    private const string EmptyMessage = "No existen especialidades disponibles";
    private readonly List<Specialty> _created = [];
    private readonly List<User> _createdUsers = [];

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
    public async Task GET_aplica_el_estado_segun_el_JWT_real_sin_revelar_inactivas_mediante_el_conteo(
        UserRole? role, string? status)
    {
        var active = await SeedAsync("Ginecología", "Salud ginecológica.");
        var inactive = await SeedAsync("Ginecología Pediátrica", "Atención pediátrica.", false);
        var other = await SeedAsync("Medicina Interna", "Atención de adultos.");
        if (role.HasValue)
        {
            await AuthorizeAsync(role.Value);
        }

        var administrator = role == UserRole.Administrator;
        Specialty[] expected = administrator ? status switch
        {
            "Inactive" => [inactive],
            "All" => [active, inactive, other],
            _ => [active, other]
        } : [active, other];

        var body = await GetAsync(status: status);

        AssertPage(body, expected, expected.Length, 1, 20, administrator);
        var matching = expected.Where(value => value.Id != other.Id).ToArray();
        AssertPage(await GetAsync("gineco", status), matching, matching.Length, 1, 20, administrator);
    }

    [Theory]
    [InlineData("gineco")]
    [InlineData("GINECO")]
    [InlineData("GINÉCO")]
    [InlineData("ginecologia")]
    [InlineData("GINECOLOGÍA")]
    [InlineData("neco")]
    [InlineData("  gineco  ")]
    public async Task GET_busca_subcadenas_sin_mayusculas_ni_tildes_en_PostgreSQL(string search)
    {
        var expected = await SeedAsync("Ginecología", "Salud ginecológica.");
        await SeedAsync("Ginecología Pediátrica", "Registro inactivo.", false);
        await SeedAsync("Medicina Interna");

        var body = await GetAsync(search, "All");

        AssertPage(body, [expected], 1, 1, 20, false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task GET_sin_texto_de_busqueda_conserva_todas_las_activas(string? search)
    {
        var first = await SeedAsync("Alfa");
        await SeedAsync("Bravo", active: false);
        var last = await SeedAsync("Charlie");

        AssertPage(await GetAsync(search), [first, last], 2, 1, 20, false);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    public async Task GET_trata_porcentaje_y_guion_bajo_como_texto_literal(string search)
    {
        var percent = await SeedAsync("Clínica 100%");
        await SeedAsync("Clínica 1000");
        var underscore = await SeedAsync("Unidad_A");
        await SeedAsync("Unidadxa");

        AssertPage(await GetAsync(search), [search == "%" ? percent : underscore], 1, 1, 20, false);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("signature")]
    [InlineData("expired")]
    public async Task GET_con_JWT_no_valido_responde_200_como_anonimo_y_no_concede_privilegios(string kind)
    {
        var active = await SeedAsync("Ginecología");
        await SeedAsync("Neurología", active: false);
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
            await SeedAsync($"Especialidad {number:00}");
        }
        await SeedAsync("Especialidad 00", active: false);
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
            await SeedAsync("Ginecología");
        }
        else if (kind == "inactive")
        {
            await SeedAsync("Ginecología", active: false);
        }

        AssertPage(await GetAsync(search: kind == "search" ? "EspecialidadInexistente" : null), [], 0, 1, 20, false);
    }

    [Theory]
    [InlineData("pageNumber=0", "Specialty.InvalidPageNumber")]
    [InlineData("pageNumber=-1", "Specialty.InvalidPageNumber")]
    [InlineData("pageSize=0", "Specialty.InvalidPageSize")]
    [InlineData("pageSize=-1", "Specialty.InvalidPageSize")]
    [InlineData("pageSize=101", "Specialty.InvalidPageSize")]
    [InlineData("pageNumber=2147483647&pageSize=100", "Specialty.PageOffsetTooLarge")]
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

    public override async Task DisposeAsync()
    {
        try
        {
            var ids = _created.Where(value => value.Id > 0).Select(value => value.Id).ToArray();
            var userIds = _createdUsers.Where(value => value.Id > 0).Select(value => value.Id).ToArray();
            if (ids.Length > 0 || userIds.Length > 0)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await using var scope = Factory.Services.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (ids.Length > 0)
                {
                    await context.Specialties.Where(value => ids.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
                    (await context.Specialties.AnyAsync(value => ids.Contains(value.Id), timeout.Token)).ShouldBeFalse();
                }
                if (userIds.Length > 0)
                {
                    await context.Users.Where(value => userIds.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
                    (await context.Users.AnyAsync(value => userIds.Contains(value.Id), timeout.Token)).ShouldBeFalse();
                }
            }
        }
        finally
        {
            await base.DisposeAsync();
        }
    }

    private async Task<Specialty> SeedAsync(string name, string description = "Atención médica.", bool active = true)
    {
        var specialty = Specialty.Create(name, description).Value;
        if (!active)
        {
            specialty.Deactivate().IsSuccess.ShouldBeTrue();
        }
        _created.Add(specialty);
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>().Add(specialty);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        specialty.Id.ShouldBeGreaterThan(0);
        return specialty;
    }

    private async Task AuthorizeAsync(UserRole role, bool expired = false)
    {
        const string password = "Secreta123";
        var now = DateTimeOffset.UtcNow;
        var options = Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        try
        {
            // El validador JWT usa tiempo real, no el reloj sustituido de la aplicación.
            Clock.UtcNow = expired
                ? now.AddMinutes(-options.AccessTokenMinutes - 5).AddSeconds(-options.ClockSkewSeconds)
                : now;
            var user = UserMother.WithRole(role, email: Email.Create($"listado-{role}@correo.com").Value,
                passwordHash: Factory.HashPassword(password));
            _createdUsers.Add(user);
            await Factory.SaveAsync(user);
            user.Id.ShouldBeGreaterThan(0);
            using var response = await LoginAsync(user.Email.Value, password);
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
        if (pageNumber.HasValue) query["pageNumber"] = pageNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (pageSize.HasValue) query["pageSize"] = pageSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var response = await Client.GetAsync(QueryHelpers.AddQueryString(Url, query));
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static void AssertPage(JsonElement body, IReadOnlyList<Specialty> expected, int total, int page, int size, bool includeStatus)
    {
        body.GetProperty("pageNumber").GetInt32().ShouldBe(page);
        body.GetProperty("pageSize").GetInt32().ShouldBe(size);
        body.GetProperty("totalCount").GetInt32().ShouldBe(total);
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        items.Select(item => item.GetProperty("id").GetInt32()).ShouldBe(expected.Select(value => value.Id));
        for (var index = 0; index < items.Length; index++)
        {
            items[index].GetProperty("name").GetString().ShouldBe(expected[index].Name);
            items[index].GetProperty("description").GetString().ShouldBe(expected[index].Description);
            items[index].TryGetProperty("isActive", out var active).ShouldBe(includeStatus);
            if (includeStatus) active.GetBoolean().ShouldBe(expected[index].IsActive);
        }
        body.TryGetProperty("message", out var message).ShouldBe(total == 0);
        if (total == 0) message.GetString().ShouldBe(EmptyMessage);
    }
}
