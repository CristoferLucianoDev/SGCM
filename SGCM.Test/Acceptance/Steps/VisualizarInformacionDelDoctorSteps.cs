using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
[Scope(Tag = "DoctorPublicProfile")]
public sealed class VisualizarInformacionDelDoctorSteps(ApiScenario scenario, InicioDeSesionSteps inicio)
{
    private const string Password = "Sintetica260";
    private static readonly string[] PrivateFields = ["phoneNumber", "licenseNumber"];
    private static readonly string[] AccountFields =
        ["userId", "email", "sessionId", "accessToken", "refreshToken", "refreshTokenHash", "passwordHash"];
    private static readonly string[] UpdatedPublicFields =
        ["province", "municipality", "addressLine", "consultationFee", "yearsOfExperience", "biography"];

    private readonly string _ownerEmail = $"medico260-{Guid.NewGuid():N}@correo.com";
    private Location[] _locations = [];
    private Reference[] _activeSpecialties = [];
    private Profile _doctor = null!;
    private Profile _otherDoctor = null!;
    private int _doctorId;
    private HttpClient? _anonymousClient;
    private HttpResponseMessage? _anonymousResponse;
    private JsonObject _before = null!;
    private UpdateDoctorProfileRequest _update = null!;

    [Given("que se publica un perfil médico con especialidades activas e inactivas")]
    public Task PublishProfileAsync() => PrepareProfileAsync(active: true);

    [Given("se publica otro perfil médico con datos distintos")]
    public async Task PublishOtherProfileAsync()
    {
        _otherDoctor = await SaveDoctorAsync($"otro-medico260-{Guid.NewGuid():N}@correo.com",
            "Marta", "LIC-260-B", "809-555-0262", _locations[1],
            _activeSpecialties.Select(s => s.Id).ToArray(), active: true);
    }

    [Given("consulto fichas médicas como {string}")]
    public async Task SelectViewerAsync(string viewer)
    {
        if (viewer == "Visitante")
        {
            scenario.Client.DefaultRequestHeaders.Authorization.ShouldBeNull();
            return;
        }

        await AuthenticatedScenario.LoginAsync(scenario, viewer, "lector-perfil260");
    }

    [When("solicito la ficha del primer médico")]
    public async Task GetProfileAsync() => scenario.Response = await scenario.Client.GetAsync(ProfileUrl);

    [Then("recibo todos sus datos públicos y solo sus especialidades activas en orden")]
    public async Task ReceivePublicProfileAsync()
    {
        _before = await ReadProfileAsync(scenario.Response!);
        AssertPublicValues(_before, _doctor, _locations[0], _doctor.Professional);
    }

    [Then("la ficha pública omite los datos privados en todo el documento")]
    public void ReceiveNoPrivateFields() => AssertPublicPrivacy(_before);

    [Then("recibo su perfil completo con su propio teléfono y licencia")]
    public async Task ReceiveAdministratorProfileAsync()
    {
        var body = await ReadProfileAsync(scenario.Response!);
        AssertNoFields(body, AccountFields);
        body.Select(field => field.Key).ShouldBe(new[] { "profile", "phoneNumber", "licenseNumber" }, ignoreOrder: true);
        AssertUsesExpectedPrivateValues(body);

        _before = body["profile"].ShouldBeOfType<JsonObject>();
        AssertPublicPrivacy(_before);
        AssertPublicValues(_before, _doctor, _locations[0], _doctor.Professional);
    }

    [Then("un visitante consulta la misma ficha y recibe solo su parte pública")]
    public async Task CompareAnonymousProfileAsync()
    {
        using var response = await GetAnonymouslyAsync();
        var body = await ReadProfileAsync(response);
        AssertPublicPrivacy(body);
        JsonNode.DeepEquals(body, _before).ShouldBeTrue("La ficha del visitante debe coincidir con la parte pública.");
    }

    [Given("un perfil médico {string}")]
    public async Task PrepareUnavailableProfileAsync(string availability)
    {
        switch (availability)
        {
            case "inactivo":
                await PrepareProfileAsync(active: false);
                break;
            case "inexistente":
                await using (var scope = scenario.Factory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    _doctorId = (await db.Doctors.MaxAsync(doctor => (int?)doctor.Id) ?? 0) + 1;
                    (await db.Doctors.AnyAsync(doctor => doctor.Id == _doctorId)).ShouldBeFalse();
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(availability), availability, "Disponibilidad desconocida.");
        }
    }

    [When("un visitante y el administrador solicitan esa misma ficha médica")]
    public async Task RequestUnavailableProfileAsync()
    {
        _anonymousResponse = await GetAnonymouslyAsync();
        await GetProfileAsync();
    }

    [Then("ambos reciben el aviso de perfil no encontrado sin datos del médico")]
    public async Task ReceiveNotFoundAsync()
    {
        await AssertNotFoundAsync(_anonymousResponse!);
        await AssertNotFoundAsync(scenario.Response!);
    }

    [Given("un visitante consulta la ficha antes de la actualización")]
    public async Task ReadInitialProfileAsync()
    {
        using var response = await GetAnonymouslyAsync();
        _before = await ReadProfileAsync(response);
        AssertPublicPrivacy(_before);
        AssertPublicValues(_before, _doctor, _locations[0], _doctor.Professional);
    }

    [When("el titular inicia sesión y guarda sus siete nuevos datos profesionales")]
    public async Task UpdateAsOwnerAsync()
    {
        await inicio.LoginAsync(_ownerEmail, Password);
        var login = await inicio.ReadSuccessfulLoginAsync();
        login.RequiresTwoFactor.ShouldBeFalse();
        login.PendingStep.ShouldBeNull();
        var jwt = InicioDeSesionSteps.ReadAccessToken(login);
        jwt.GetClaim(AppClaims.Role).Value.ShouldBe(nameof(UserRole.Doctor));

        var location = _locations[1];
        _update = new UpdateDoctorProfileRequest("809-555-0263", location.ProvinceId, location.MunicipalityId,
            "Avenida de la Salud 260", 1850.75m, 16, "Atención preventiva y seguimiento clínico.");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/doctors/me/profile")
        {
            Content = JsonContent.Create(_update)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.SendAsync(request);
    }

    [Then("la actualización se confirma sin contenido")]
    public async Task ReceiveEmptyConfirmationAsync()
    {
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await scenario.Response.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
    }

    [Then("el mismo visitante ve de inmediato los cambios en la ficha pública")]
    public async Task ReadUpdatedProfileAsync()
    {
        using var response = await GetAnonymouslyAsync();
        var after = await ReadProfileAsync(response);
        AssertPublicPrivacy(after);
        AssertPublicValues(after, _doctor, _locations[1], _update);
        foreach (var field in UpdatedPublicFields)
            JsonNode.DeepEquals(after[field], _before[field]).ShouldBeFalse($"El campo {field} debe cambiar.");
        foreach (var field in new[] { "id", "fullName", "specialties" })
            JsonNode.DeepEquals(after[field], _before[field]).ShouldBeTrue($"El campo {field} debe conservarse.");
    }

    [AfterScenario("DoctorPublicProfile")]
    public async Task ReleaseScenarioResourcesAsync()
    {
        _anonymousResponse?.Dispose();
        _anonymousClient?.Dispose();
        // Hay pruebas del catálogo que comparten la base y esperan empezar sin especialidades.
        await SharedApp.ResetAsync();
    }

    private string ProfileUrl => $"/api/doctors/{_doctorId}/profile";

    private async Task PrepareProfileAsync(bool active)
    {
        scenario.Factory.Clock.UtcNow = DateTimeOffset.UtcNow;
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _locations = await db.Database.SqlQuery<Location>($"""
            SELECT DISTINCT ON (p.id) p.id AS province_id, p.name AS province_name,
                m.id AS municipality_id, m.name AS municipality_name
            FROM people.province p JOIN people.municipality m ON m.province_id = p.id
            ORDER BY p.id, m.id LIMIT 2
            """).ToArrayAsync();
        _locations.Length.ShouldBe(2);

        // La última del orden público se guarda primero.
        var last = Specialty.Create("Traumatología 260", "Especialidad activa.").Value;
        db.Specialties.Add(last);
        await db.SaveChangesAsync();
        var first = Specialty.Create("Cardiología 260", "Especialidad activa.").Value;
        var inactive = Specialty.Create("Medicina interna 260", "Especialidad inactiva.").Value;
        inactive.Deactivate().IsSuccess.ShouldBeTrue();
        db.Specialties.AddRange(first, inactive);
        await db.SaveChangesAsync();
        _activeSpecialties = [new(first.Id, first.Name), new(last.Id, last.Name)];
        _doctor = await SaveDoctorAsync(_ownerEmail, "Ana", "LIC-260-A", "809-555-0261",
            _locations[0], [last.Id, first.Id, inactive.Id], active);
        _doctorId = _doctor.Id;
    }

    private async Task<Profile> SaveDoctorAsync(string email, string firstName, string license,
        string phone, Location location, int[] specialties, bool active)
    {
        await inicio.DadoUnMedico(email, Password);
        var user = (await scenario.Factory.FindUserAsync(email)).ShouldNotBeNull();
        var doctor = DoctorMother.Create(specialties, userId: user.Id, firstName: firstName, lastName: "Núñez",
            licenseNumber: license, phoneNumber: phone, provinceId: location.ProvinceId,
            municipalityId: location.MunicipalityId).Value;
        if (!active)
            doctor.Deactivate().IsSuccess.ShouldBeTrue();

        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        return new Profile(doctor.Id, $"{firstName} Núñez", license,
            new UpdateDoctorProfileRequest(phone, location.ProvinceId, location.MunicipalityId,
                DoctorMother.AddressLine, DoctorMother.ConsultationFee, DoctorMother.YearsOfExperience,
                DoctorMother.Biography));
    }

    private Task<HttpResponseMessage> GetAnonymouslyAsync()
    {
        _anonymousClient ??= scenario.Factory.CreateClient();
        _anonymousClient.DefaultRequestHeaders.Authorization.ShouldBeNull();
        return _anonymousClient.GetAsync(ProfileUrl);
    }

    private static async Task<JsonObject> ReadProfileAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        var policy = response.Headers.CacheControl.ShouldNotBeNull();
        policy.NoStore.ShouldBeTrue();
        policy.Public.ShouldBeFalse();
        policy.SharedMaxAge.ShouldBeNull();
        return (await response.Content.ReadFromJsonAsync<JsonObject>()).ShouldNotBeNull();
    }

    private void AssertPublicValues(JsonObject actual, Profile doctor, Location location,
        UpdateDoctorProfileRequest professional)
    {
        var expected = new JsonObject
        {
            ["id"] = doctor.Id,
            ["fullName"] = doctor.FullName,
            ["specialties"] = new JsonArray(_activeSpecialties.Select(s => (JsonNode)ReferenceJson(s.Id, s.Name)).ToArray()),
            ["province"] = ReferenceJson(location.ProvinceId, location.ProvinceName),
            ["municipality"] = ReferenceJson(location.MunicipalityId, location.MunicipalityName),
            ["addressLine"] = professional.AddressLine,
            ["consultationFee"] = professional.ConsultationFee,
            ["yearsOfExperience"] = professional.YearsOfExperience,
            ["biography"] = professional.Biography
        };
        actual.Select(field => field.Key).ShouldBe(expected.Select(field => field.Key), ignoreOrder: true);
        foreach (var (field, value) in expected)
            JsonNode.DeepEquals(actual[field], value).ShouldBeTrue($"El contenido de {field} debe coincidir.");
    }

    private void AssertUsesExpectedPrivateValues(JsonObject body)
    {
        var phone = body["phoneNumber"]?.GetValue<string>();
        var license = body["licenseNumber"]?.GetValue<string>();
        (phone == _doctor.Professional.PhoneNumber).ShouldBeTrue("El teléfono debe corresponder al primer médico.");
        (license == _doctor.License).ShouldBeTrue("La licencia debe corresponder al primer médico.");
        var json = body.ToJsonString();
        json.Contains(_otherDoctor.Professional.PhoneNumber.ShouldNotBeNull(), StringComparison.Ordinal)
            .ShouldBeFalse("La ficha no debe incluir el teléfono de otro médico.");
        json.Contains(_otherDoctor.License, StringComparison.Ordinal)
            .ShouldBeFalse("La ficha no debe incluir la licencia de otro médico.");
    }

    private static async Task AssertNotFoundAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>()).ShouldNotBeNull();
        AssertPublicPrivacy(body);
        body.Select(field => field.Key).ShouldBe(new[] { "title", "status", "code" }, ignoreOrder: true);
        body["status"].ShouldNotBeNull().GetValue<int>().ShouldBe((int)HttpStatusCode.NotFound);
        (body["code"].ShouldNotBeNull().GetValue<string>() == "Doctor.PublicProfileNotFound")
            .ShouldBeTrue("Se esperaba el código Doctor.PublicProfileNotFound.");
        (body["title"].ShouldNotBeNull().GetValue<string>() == "No se encontró el perfil público del médico solicitado.")
            .ShouldBeTrue("El mensaje público debe ser el mismo para un perfil inexistente o inactivo.");
    }

    private static JsonObject ReferenceJson(int id, string name) => new() { ["id"] = id, ["name"] = name };

    private static void AssertPublicPrivacy(JsonNode body) => AssertNoFields(body, [..PrivateFields, ..AccountFields]);

    private static void AssertNoFields(JsonNode? node, string[] forbidden)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (field, value) in obj)
                {
                    forbidden.Contains(field, StringComparer.OrdinalIgnoreCase)
                        .ShouldBeFalse($"La propiedad {field} debe estar ausente del JSON.");
                    AssertNoFields(value, forbidden);
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    AssertNoFields(item, forbidden);
                break;
        }
    }

    private sealed record Profile(int Id, string FullName, string License, UpdateDoctorProfileRequest Professional);
    private sealed record Location(int ProvinceId, string ProvinceName, int MunicipalityId, string MunicipalityName);
    private sealed record Reference(int Id, string Name);
}
