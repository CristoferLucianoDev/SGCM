using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Reqnroll;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
[Scope(Tag = "DoctorSearch")]
public sealed class BuscarDoctoresSteps(ApiScenario scenario)
{
    private const string SearchUrl = "/api/doctors";
    private const string ProvincesUrl = "/api/locations/provinces";
    private readonly Dictionary<string, Specialty> _specialties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SeededDoctor> _doctors = new(StringComparer.Ordinal);
    private readonly Dictionary<int, JsonObject> _pages = [];
    private Location[] _locations = [];
    private int _selectedProvinceId;
    private int _selectedMunicipalityId;
    private int _foundDoctorId;
    private JsonObject _searchPage = null!;
    private JsonObject _profile = null!;

    [Given("un catálogo de especialidades con:")]
    public async Task PrepareSpecialtiesAsync(Table table)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var row in table.Rows)
        {
            var specialty = Specialty.Create(row["Especialidad"], "Especialidad de prueba 236.").Value;
            if (!IsActive(row["Estado"])) specialty.Deactivate().IsSuccess.ShouldBeTrue();
            _specialties.Add(specialty.Name, specialty);
            db.Specialties.Add(specialty);
        }

        await db.SaveChangesAsync();
        _specialties.Values.ShouldAllBe(specialty => specialty.Id > 0);
        var specialtyIds = _specialties.Values.Select(specialty => specialty.Id).ToArray();
        var persistedStates = await db.Specialties.Where(specialty => specialtyIds.Contains(specialty.Id))
            .Select(specialty => new { specialty.Id, specialty.IsActive })
            .ToDictionaryAsync(specialty => specialty.Id, specialty => specialty.IsActive);
        foreach (var row in table.Rows)
        {
            var name = row["Especialidad"];
            persistedStates[_specialties[name].Id].ShouldBe(IsActive(row["Estado"]), $"Estado de {name}");
        }
    }

    [Given("un directorio médico con:")]
    public async Task PrepareDirectoryAsync(Table table)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _locations = await db.Database.SqlQuery<Location>($"""
            SELECT p.id AS province_id, p.name AS province_name,
                   m.id AS municipality_id, m.name AS municipality_name
            FROM people.province p JOIN people.municipality m ON m.province_id = p.id
            """).ToArrayAsync();
        var accounts = table.Rows.Select(row =>
        {
            var user = UserMother.WithRole(UserRole.Doctor,
                email: Email.Create($"medico236-{Guid.NewGuid():N}@example.com").Value);
            if (!IsActive(row["Cuenta"])) user.Deactivate().IsSuccess.ShouldBeTrue();
            db.Users.Add(user);
            return (Row: row, User: user);
        }).ToArray();
        await db.SaveChangesAsync();

        var legacyProfiles = new List<Doctor>();
        foreach (var (row, user) in accounts)
        {
            var location = FindLocation(row["Provincia"], row["Municipio"]);
            var names = row["Nombre completo"].Split(' ', 2);
            var specialties = row["Especialidades"] == "Sin especialidades"
                ? Array.Empty<Specialty>()
                : SplitNames(row["Especialidades"]).Select(name => _specialties[name]).ToArray();
            // El dominio exige una asociación al crear el perfil. El dato legado se ajusta después de guardarlo.
            var specialtyIds = specialties.Length == 0
                ? [_specialties["Cardiología"].Id]
                : specialties.Select(specialty => specialty.Id).ToArray();
            var doctor = DoctorMother.Create(specialtyIds, userId: user.Id, firstName: names[0], lastName: names[1],
                licenseNumber: $"LIC-236-{user.Id}", addressLine: $"Calle de prueba {row["Médico"]} 236",
                provinceId: location.ProvinceId, municipalityId: location.MunicipalityId,
                consultationFee: decimal.Parse(row["Tarifa"], CultureInfo.InvariantCulture)).Value;
            if (!IsActive(row["Perfil"])) doctor.Deactivate().IsSuccess.ShouldBeTrue();
            doctor.PhoneNumber.Value.ShouldNotBeNullOrWhiteSpace();
            doctor.LicenseNumber.ShouldNotBeNullOrWhiteSpace();
            db.Doctors.Add(doctor);
            if (specialties.Length == 0) legacyProfiles.Add(doctor);
            _doctors.Add(row["Médico"], new(doctor, location,
                specialties.Where(specialty => specialty.IsActive).OrderBy(specialty => specialty.Name, StringComparer.Ordinal).ToArray()));
        }

        await db.SaveChangesAsync();
        _doctors.Values.ShouldAllBe(entry => entry.Doctor.Id > 0);
        foreach (var doctor in legacyProfiles)
        {
            (await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM people.doctor_specialty WHERE doctor_id = {doctor.Id}")).ShouldBe(1);
        }

        await AssertPersistedDirectoryAsync(db, table);
    }

    [Given("que consulto el directorio sin iniciar sesión")]
    public void UseAnonymousClient() => scenario.Client.DefaultRequestHeaders.Authorization.ShouldBeNull();

    [Given("que consulto el directorio como {string}")]
    public async Task LoginAsync(string actor)
    {
        var role = actor switch
        {
            "Paciente" => UserRole.Patient,
            "Médico" => UserRole.Doctor,
            "Administrador" => UserRole.Administrator,
            _ => throw new ArgumentOutOfRangeException(nameof(actor), actor, "Actor de prueba desconocido.")
        };
        var userId = 0;
        await AuthenticatedScenario.LoginAsync(scenario, role.ToString(), "lector236", id => userId = id);
        var authorization = scenario.Client.DefaultRequestHeaders.Authorization.ShouldNotBeNull();
        authorization.Scheme.ShouldBe("Bearer");
        var token = authorization.Parameter.ShouldNotBeNull();
        var validation = scenario.Factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        (await new JsonWebTokenHandler().ValidateTokenAsync(token, validation)).IsValid.ShouldBeTrue();
        var jwt = new JsonWebToken(token);
        jwt.GetClaim(AppClaims.UserId).Value.ShouldBe(Number(userId));
        var sessionId = int.Parse(jwt.GetClaim(AppClaims.SessionId).Value, CultureInfo.InvariantCulture);

        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.Include(account => account.Sessions).SingleAsync(account => account.Id == userId);
        user.Role.ShouldBe(role);
        user.IsActive.ShouldBeTrue();
        user.Sessions.Single(session => session.Id == sessionId).IsActiveAt(scenario.Factory.Clock.UtcNow).ShouldBeTrue();
    }

    [When("busco médicos sin aplicar filtros")]
    public async Task SearchWithoutFiltersAsync() => _searchPage = await GetPageAsync(scenario.Client, SearchUrl);

    [When("busco médicos solo por {string} con valor {string}")]
    public async Task SearchWithOneFilterAsync(string filter, string value)
    {
        var (parameter, id) = filter switch
        {
            "especialidad" => ("specialtyId", _specialties[value].Id),
            "provincia" => ("provinceId", _locations.Where(location => location.ProvinceName == value)
                .Select(location => location.ProvinceId).Distinct().Single()),
            "municipio" => ("municipalityId", _locations.Single(location => location.MunicipalityName == value).MunicipalityId),
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Filtro de prueba desconocido.")
        };
        _searchPage = await GetPageAsync(scenario.Client, QueryHelpers.AddQueryString(SearchUrl, parameter, Number(id)),
            (parameter, Number(id)));
    }

    [When("selecciono en los catálogos la provincia {string} y su municipio {string}")]
    public async Task SelectLocationAsync(string province, string municipality)
    {
        var provinces = await GetJsonAsync<JsonArray>(scenario.Client, ProvincesUrl);
        _selectedProvinceId = SelectCatalogId(provinces, province);
        var municipalities = await GetJsonAsync<JsonArray>(scenario.Client, $"{ProvincesUrl}/{_selectedProvinceId}/municipalities");
        _selectedMunicipalityId = SelectCatalogId(municipalities, municipality);
    }

    [When("busco médicos de {string} en esa ubicación en las páginas {int}, {int} y {int} de tamaño {int}")]
    public async Task SearchLocationPagesAsync(string specialty, int first, int second, int third, int size)
    {
        var expectedLocation = ExpectedLocationQuery(specialty, _selectedProvinceId, _selectedMunicipalityId);
        var filters = LocationFilters(specialty, _selectedProvinceId, _selectedMunicipalityId);
        filters["pageSize"] = Number(size);
        foreach (var number in new[] { first, second, third })
        {
            filters["pageNumber"] = Number(number);
            var page = await GetPageAsync(scenario.Client, QueryHelpers.AddQueryString(SearchUrl, filters),
                [.. expectedLocation, ("pageNumber", Number(number)), ("pageSize", Number(size))]);
            AssertPagination(page, number, size);
            _pages.Add(number, page);
        }
    }

    [When("busco {string} en el municipio {string} de la provincia {string}")]
    public async Task SearchWithThreeFiltersAsync(string specialty, string municipality, string province)
    {
        var location = FindLocation(province, municipality);
        var filters = LocationFilters(specialty, location.ProvinceId, location.MunicipalityId);
        _searchPage = await GetPageAsync(scenario.Client, QueryHelpers.AddQueryString(SearchUrl, filters),
            ExpectedLocationQuery(specialty, location.ProvinceId, location.MunicipalityId));
    }

    [When("busco médicos por el nombre {string}")]
    public async Task SearchByNameAsync(string name) =>
        _searchPage = await GetPageAsync(scenario.Client, QueryHelpers.AddQueryString(SearchUrl, "search", name), ("search", name));

    [When("abro la ficha de la única coincidencia")]
    public async Task OpenFoundProfileAsync()
    {
        _searchPage["totalCount"].ShouldNotBeNull().GetValue<int>().ShouldBe(1);
        _foundDoctorId = Items(_searchPage).ShouldHaveSingleItem().ShouldNotBeNull()["id"].ShouldNotBeNull().GetValue<int>();
        _profile = await GetJsonAsync<JsonObject>(scenario.Client, $"{SearchUrl}/{_foundDoctorId}/profile");
    }

    [Then("recibo la página {int} de tamaño {int} con total {int}")]
    public void ReceivePage(int number, int size, int total)
    {
        AssertPagination(_searchPage, number, size);
        _searchPage["totalCount"].ShouldNotBeNull().GetValue<int>().ShouldBe(total);
    }

    [Then("aparecen únicamente {string} en ese orden")]
    public void ReceiveDoctors(string doctors) => AssertDoctorIds(_searchPage, SplitNames(doctors));

    [Then("cada resultado muestra únicamente la información pública de búsqueda")]
    public void ReceiveOnlyPublicFields()
    {
        var items = Items(_searchPage);
        items.ShouldNotBeEmpty();
        foreach (var item in items)
        {
            var doctor = item.ShouldBeOfType<JsonObject>();
            var id = doctor["id"].ShouldNotBeNull().GetValue<int>();
            var expected = _doctors.Values.Single(entry => entry.Doctor.Id == id);
            AssertObject(doctor, ExpectedSearchItem(expected));
        }
    }

    [Then("la primera página contiene únicamente a {string}")]
    public void ReceiveFirstPage(string doctor) => AssertDoctorIds(_pages[1], [doctor]);

    [Then("la segunda página contiene únicamente a {string}")]
    public void ReceiveSecondPage(string doctor) => AssertDoctorIds(_pages[2], [doctor]);

    [Then("las tres páginas conservan el total de {int} médicos")]
    public void ReceiveFilteredTotal(int total)
    {
        foreach (var (number, page) in _pages)
        {
            page["totalCount"].ShouldNotBeNull().GetValue<int>().ShouldBe(total, $"Total de la página {number}");
        }
        var ids = _pages.Values.SelectMany(DoctorIds).ToArray();
        ids.Distinct().Count().ShouldBe(ids.Length);
    }

    [Then("la tercera página se devuelve correctamente sin médicos")]
    public void ReceiveEmptyThirdPage() => Items(_pages[3]).ShouldBeEmpty();

    [Then("el resultado de {string} contiene exactamente {string} en ese orden")]
    public void ReceiveActiveSpecialties(string doctor, string specialties)
    {
        var id = _doctors[doctor].Doctor.Id;
        var item = _pages.Values.SelectMany(page => Items(page)).OfType<JsonObject>()
            .Single(result => result["id"].ShouldNotBeNull().GetValue<int>() == id);
        var expected = new JsonArray(SplitNames(specialties).Select(name =>
            (JsonNode)ReferenceJson(_specialties[name].Id, name)).ToArray());
        JsonNode.DeepEquals(item["specialties"], expected).ShouldBeTrue(
            "La búsqueda debe incluir todos los pares de especialidades activas en orden.");
    }

    [Then("recibo la misma página que un visitante al hacer esa búsqueda")]
    public async Task ReceiveSamePageAsVisitorAsync()
    {
        using var anonymous = scenario.Factory.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization.ShouldBeNull();
        var reference = await GetPageAsync(anonymous, SearchUrl);
        AssertPagination(reference, 1, 20);
        reference["totalCount"].ShouldNotBeNull().GetValue<int>().ShouldBe(5);
        AssertDoctorIds(reference, ["Alba", "Bruno", "Clara", "Diego", "Elena"]);
        JsonNode.DeepEquals(_searchPage, reference).ShouldBeTrue("La sesión no debe cambiar la proyección pública.");
    }

    [Then("recibo la página {int} de tamaño {int} sin médicos y con total {int}")]
    public void ReceiveNoMatches(int number, int size, int total)
    {
        ReceivePage(number, size, total);
        Items(_searchPage).ShouldBeEmpty();
    }

    [Then("recibo la ficha pública completa de {string}")]
    public void ReceivePublicProfile(string name)
    {
        var entry = _doctors.Values.Single(item => $"{item.Doctor.FirstName} {item.Doctor.LastName}" == name);
        _foundDoctorId.ShouldBe(entry.Doctor.Id);
        AssertObject(Items(_searchPage).ShouldHaveSingleItem().ShouldBeOfType<JsonObject>(), ExpectedSearchItem(entry));
        var expected = ExpectedSearchItem(entry);
        expected["addressLine"] = entry.Doctor.AddressLine;
        expected["yearsOfExperience"] = entry.Doctor.YearsOfExperience;
        expected["biography"] = entry.Doctor.Biography;
        AssertObject(_profile, expected);
    }

    [Then("la ficha corresponde al mismo médico identificado en el listado")]
    public void ProfileMatchesSearchResult() => _profile["id"].ShouldNotBeNull().GetValue<int>().ShouldBe(_foundDoctorId);

    [Then("la ficha omite el teléfono y la licencia")]
    public void ProfileOmitsPrivateFields()
    {
        _profile.ContainsKey("phoneNumber").ShouldBeFalse();
        _profile.ContainsKey("licenseNumber").ShouldBeFalse();
    }

    [AfterScenario("DoctorSearch")]
    public Task ResetScenarioAsync() => SharedApp.ResetAsync();

    private async Task AssertPersistedDirectoryAsync(AppDbContext db, Table table)
    {
        var doctorIds = _doctors.Values.Select(entry => entry.Doctor.Id).ToArray();
        var profiles = await db.Doctors.AsNoTracking().Where(doctor => doctorIds.Contains(doctor.Id))
            .Include(doctor => doctor.Specialties).ToDictionaryAsync(doctor => doctor.Id);
        var userIds = _doctors.Values.Select(entry => entry.Doctor.UserId).ToArray();
        var accountStates = await db.Users.Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.IsActive }).ToDictionaryAsync(user => user.Id, user => user.IsActive);

        foreach (var row in table.Rows)
        {
            var alias = row["Médico"];
            var entry = _doctors[alias];
            var profile = profiles[entry.Doctor.Id];
            profile.IsActive.ShouldBe(IsActive(row["Perfil"]), $"Estado del perfil de {alias}");
            profile.UserId.ShouldBe(entry.Doctor.UserId);
            accountStates[profile.UserId].ShouldBe(IsActive(row["Cuenta"]), $"Estado de la cuenta de {alias}");
            var specialtyIds = row["Especialidades"] == "Sin especialidades"
                ? Array.Empty<int>()
                : SplitNames(row["Especialidades"]).Select(name => _specialties[name].Id).ToArray();
            profile.Specialties.Select(association => association.SpecialtyId)
                .ShouldBe(specialtyIds, ignoreOrder: true);
            $"{profile.FirstName} {profile.LastName}".ShouldBe(row["Nombre completo"]);
            profile.ProvinceId.ShouldBe(entry.Location.ProvinceId);
            profile.MunicipalityId.ShouldBe(entry.Location.MunicipalityId);
            profile.ConsultationFee.Value.ShouldBe(decimal.Parse(row["Tarifa"], CultureInfo.InvariantCulture));
            profile.PhoneNumber.Value.ShouldBe(entry.Doctor.PhoneNumber.Value);
            profile.LicenseNumber.ShouldBe(entry.Doctor.LicenseNumber);
        }
    }

    private Location FindLocation(string province, string municipality) =>
        _locations.Single(location => location.ProvinceName == province && location.MunicipalityName == municipality);

    private Dictionary<string, string?> LocationFilters(string specialty, int provinceId, int municipalityId) => new()
    {
        ["specialtyId"] = Number(_specialties[specialty].Id),
        ["provinceId"] = Number(provinceId),
        ["municipalityId"] = Number(municipalityId)
    };

    private (string Parameter, string Value)[] ExpectedLocationQuery(string specialty, int provinceId, int municipalityId) =>
    [
        ("specialtyId", Number(_specialties[specialty].Id)),
        ("provinceId", Number(provinceId)),
        ("municipalityId", Number(municipalityId))
    ];

    private static int SelectCatalogId(JsonArray catalog, string name)
    {
        var result = catalog.OfType<JsonObject>().Single(item => item["name"]?.GetValue<string>() == name);
        var id = result["id"].ShouldNotBeNull().GetValue<int>();
        id.ShouldBeGreaterThan(0);
        return id;
    }

    private static async Task<JsonObject> GetPageAsync(HttpClient client, string url,
        params (string Parameter, string Value)[] expectedQuery)
    {
        using var response = await client.GetAsync(url);
        var page = await ReadJsonAsync<JsonObject>(response);
        var requestUri = response.RequestMessage.ShouldNotBeNull().RequestUri.ShouldNotBeNull();
        requestUri.AbsolutePath.ShouldBe(SearchUrl);
        var query = QueryHelpers.ParseQuery(requestUri.Query);
        query.Keys.ShouldBe(expectedQuery.Select(field => field.Parameter), ignoreOrder: true);
        foreach (var (parameter, value) in expectedQuery)
        {
            query[parameter].ToArray().ShouldBe(new[] { value }, $"Valor enviado de {parameter}");
        }

        page.Select(field => field.Key).ShouldBe(new[] { "items", "pageNumber", "pageSize", "totalCount" }, ignoreOrder: true);
        return page;
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string url) where T : JsonNode
    {
        using var response = await client.GetAsync(url);
        return await ReadJsonAsync<T>(response);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response) where T : JsonNode
    {
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        return (await response.Content.ReadFromJsonAsync<T>()).ShouldNotBeNull();
    }

    private void AssertDoctorIds(JsonObject page, string[] doctors) =>
        DoctorIds(page).ShouldBe(doctors.Select(alias => _doctors[alias].Doctor.Id));

    private static IEnumerable<int> DoctorIds(JsonObject page) =>
        Items(page).Select(item => item.ShouldNotBeNull()["id"].ShouldNotBeNull().GetValue<int>());

    private static JsonArray Items(JsonObject page) => page["items"].ShouldBeOfType<JsonArray>();

    private static void AssertPagination(JsonObject page, int number, int size)
    {
        page["pageNumber"].ShouldNotBeNull().GetValue<int>().ShouldBe(number);
        page["pageSize"].ShouldNotBeNull().GetValue<int>().ShouldBe(size);
    }

    private static JsonObject ExpectedSearchItem(SeededDoctor entry) => new()
    {
        ["id"] = entry.Doctor.Id,
        ["fullName"] = $"{entry.Doctor.FirstName} {entry.Doctor.LastName}",
        ["specialties"] = new JsonArray(entry.ActiveSpecialties.Select(specialty =>
            (JsonNode)ReferenceJson(specialty.Id, specialty.Name)).ToArray()),
        ["province"] = ReferenceJson(entry.Location.ProvinceId, entry.Location.ProvinceName),
        ["municipality"] = ReferenceJson(entry.Location.MunicipalityId, entry.Location.MunicipalityName),
        ["consultationFee"] = entry.Doctor.ConsultationFee.Value
    };

    private static void AssertObject(JsonObject actual, JsonObject expected)
    {
        actual.Select(field => field.Key).ShouldBe(expected.Select(field => field.Key), ignoreOrder: true);
        foreach (var (field, value) in expected)
        {
            JsonNode.DeepEquals(actual[field], value).ShouldBeTrue($"El campo {field} debe coincidir con el dato preparado.");
        }
    }

    private static JsonObject ReferenceJson(int id, string name) => new() { ["id"] = id, ["name"] = name };

    private static string[] SplitNames(string names) => names.Split(',', StringSplitOptions.TrimEntries);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool IsActive(string status) => status switch
    {
        "Activo" or "Activa" => true,
        "Inactivo" or "Inactiva" => false,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Estado de prueba desconocido.")
    };

    private sealed record Location(int ProvinceId, string ProvinceName, int MunicipalityId, string MunicipalityName);
    private sealed record SeededDoctor(Doctor Doctor, Location Location, Specialty[] ActiveSpecialties);
}
