using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Reqnroll;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class CompletarPerfilDelPacienteSteps(ApiScenario scenario) : IDisposable
{
    private const string ProfileUrl = "/api/patients/profile";
    private const string LoginUrl = "/api/auth/login";

    private readonly Dictionary<string, int> _insuranceProviderIds = [];
    private string? _email;
    private string? _accessToken;
    private LoginResponse? _newTokens;
    private HttpClient? _protectedClient;
    private HttpResponseMessage? _protectedResponse;

    [Given("que inicié sesión con el correo {string} y la contraseña {string}")]
    public async Task DadoQueInicieSesion(string email, string password)
    {
        // Los access tokens vencen según el reloj real, así que la app emite con la hora actual.
        scenario.Factory.Clock.UtcNow = DateTimeOffset.UtcNow;

        using var response = await scenario.Client.PostAsJsonAsync(LoginUrl, new { email, password });
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var login = await response.ReadLoginAsync();
        login.PendingStep.ShouldBe(PendingStepClaimValues.PatientProfile);

        _email = email;
        UseAccessToken(login.AccessToken);
    }

    [Given("que existe el proveedor de seguros activo {string}")]
    public Task DadoUnProveedorActivo(string name) => InsertInsuranceProviderAsync(name, isActive: true);

    [Given("que existe el proveedor de seguros inactivo {string}")]
    public Task DadoUnProveedorInactivo(string name) => InsertInsuranceProviderAsync(name, isActive: false);

    [Given("que ya completé mi perfil")]
    public async Task DadoQueYaCompleteMiPerfil()
    {
        await SendProfileAsync(CompletePatientProfileRequestMother.WithRequiredData());
        UseAccessToken((await ReadNewTokensAsync()).AccessToken);
    }

    [When("envío mi perfil con solo los datos obligatorios")]
    public Task EnvioMiPerfilConLosDatosObligatorios() =>
        SendProfileAsync(CompletePatientProfileRequestMother.WithRequiredData());

    [When("envío mi perfil con todos los datos y el proveedor {string}")]
    public Task EnvioMiPerfilConTodosLosDatos(string providerName) =>
        SendProfileAsync(CompletePatientProfileRequestMother.WithAllData(_insuranceProviderIds[providerName]));

    [When("envío mi perfil con el proveedor {string} y sin número de póliza")]
    public Task EnvioMiPerfilSinPoliza(string providerName) =>
        SendProfileAsync(CompletePatientProfileRequestMother.WithRequiredData() with
        {
            InsuranceProviderId = _insuranceProviderIds[providerName]
        });

    [When("envío mi perfil con fecha de nacimiento de mañana")]
    public Task EnvioMiPerfilConFechaFutura()
    {
        var tomorrow = DateOnly.FromDateTime(scenario.Factory.Clock.UtcNow.UtcDateTime).AddDays(1);

        return SendProfileAsync(CompletePatientProfileRequestMother.WithRequiredData() with { BirthDate = tomorrow });
    }

    [When(@"^envío mi perfil sin (el nombre|el apellido|el teléfono|la fecha de nacimiento)$")]
    public Task EnvioMiPerfilSinUnDatoObligatorio(string field)
    {
        var valid = CompletePatientProfileRequestMother.WithRequiredData();

        return SendProfileAsync(field switch
        {
            "el nombre" => valid with { FirstName = null! },
            "el apellido" => valid with { LastName = null! },
            "el teléfono" => valid with { PhoneNumber = null! },
            _ => valid with { BirthDate = default }
        });
    }

    [When(@"^envío mi perfil con (el nombre|el apellido|el teléfono|la dirección) de (\d+) caracteres$")]
    public Task EnvioMiPerfilConUnCampoLargo(string field, int length)
    {
        var valid = CompletePatientProfileRequestMother.WithRequiredData();

        return SendProfileAsync(field switch
        {
            "el nombre" => valid with { FirstName = new string('A', length) },
            "el apellido" => valid with { LastName = new string('A', length) },
            "el teléfono" => valid with { PhoneNumber = new string('1', length) },
            _ => valid with { Address = new string('A', length) }
        });
    }

    [When("intento usar otra función de la app")]
    public Task IntentoUsarOtraFuncion() => CallProtectedEndpointAsync(_accessToken);

    [When("intento usar otra función de la app con los tokens nuevos")]
    public async Task IntentoUsarOtraFuncionConLosTokensNuevos() =>
        await CallProtectedEndpointAsync((await ReadNewTokensAsync()).AccessToken);

    [Then("mi perfil es aceptado y recibo tokens sin paso pendiente")]
    public async Task MiPerfilEsAceptado()
    {
        var tokens = await ReadNewTokensAsync();

        tokens.RequiresTwoFactor.ShouldBeFalse();
        tokens.AccessToken.ShouldNotBeNullOrEmpty();
        tokens.RefreshToken.ShouldNotBeNullOrEmpty();
        tokens.PendingStep.ShouldBeNull();
        new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken!)
            .TryGetClaim(AppClaims.PendingStep, out _).ShouldBeFalse();
    }

    [Then("mi perfil quedó guardado sin dirección, contacto de emergencia ni seguro")]
    public async Task MiPerfilQuedoGuardadoSinDatosOpcionales()
    {
        var patient = await FindMyPatientAsync();

        patient.FirstName.ShouldBe(PatientMother.DefaultFirstName);
        patient.LastName.ShouldBe(PatientMother.DefaultLastName);
        patient.BirthDate.ShouldBe(PatientMother.DefaultBirthDate);
        patient.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        patient.Address.ShouldBeNull();
        patient.EmergencyContact.ShouldBeNull();
        patient.InsuranceProviderId.ShouldBeNull();
        patient.PolicyNumber.ShouldBeNull();
    }

    [Then("mi perfil quedó guardado con el proveedor {string} y su número de póliza")]
    public async Task MiPerfilQuedoGuardadoConElProveedor(string providerName)
    {
        var patient = await FindMyPatientAsync();

        patient.InsuranceProviderId.ShouldBe(_insuranceProviderIds[providerName]);
        patient.PolicyNumber.ShouldBe(PatientMother.DefaultPolicyNumber);
        patient.EmergencyContact.ShouldNotBeNull();
        patient.EmergencyContact.Name.ShouldBe(PatientMother.DefaultEmergencyContactName);
    }

    [Then("mi perfil es rechazado con el error {string}")]
    public async Task MiPerfilEsRechazado(string errorCode)
    {
        var expectedStatus = errorCode == PatientErrors.ProfileAlreadyExists.Code
            ? HttpStatusCode.Conflict
            : HttpStatusCode.BadRequest;

        await scenario.Response!.ShouldHaveStatusAsync(expectedStatus);
        (await scenario.Response!.ReadErrorCodeAsync()).ShouldBe(errorCode);
    }

    [Then("no se guardó mi perfil")]
    public async Task NoSeGuardoMiPerfil()
    {
        using var scope = scenario.Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Patients.CountAsync()).ShouldBe(0);
    }

    [Then("la otra función responde HTTP {int}")]
    public async Task LaOtraFuncionResponde(int status) =>
        await _protectedResponse!.ShouldHaveStatusAsync((HttpStatusCode)status);

    public void Dispose()
    {
        _protectedResponse?.Dispose();
        _protectedClient?.Dispose();
    }

    private void UseAccessToken(string? accessToken)
    {
        _accessToken = accessToken;
        scenario.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private async Task SendProfileAsync(CompletePatientProfileRequest request)
    {
        scenario.Response?.Dispose();
        _newTokens = null;
        scenario.Response = await scenario.Client.PostAsJsonAsync(ProfileUrl, request);
    }

    // Los tokens nuevos son los de la última respuesta del perfil; se leen una sola vez.
    private async Task<LoginResponse> ReadNewTokensAsync()
    {
        if (_newTokens is null)
        {
            await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);
            _newTokens = await scenario.Response!.ReadLoginAsync();
        }

        return _newTokens;
    }

    private async Task CallProtectedEndpointAsync(string? accessToken)
    {
        _protectedClient ??= (await ProtectedEndpointApp.GetAsync()).CreateClient();
        _protectedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        _protectedResponse?.Dispose();
        _protectedResponse = await _protectedClient.GetAsync(ProtectedTestController.RoutePath);
    }

    private async Task InsertInsuranceProviderAsync(string name, bool isActive)
    {
        using var scope = scenario.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await TestDataSeeder.InsertInsuranceProviderAsync(context, name);
        if (!isActive)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE people.insurance_provider SET is_active = false WHERE id = {id}");
        }

        _insuranceProviderIds[name] = id;
    }

    private async Task<Patient> FindMyPatientAsync()
    {
        var user = await scenario.Factory.FindUserAsync(_email!)
            ?? throw new InvalidOperationException($"No hay un usuario con el correo {_email}.");

        using var scope = scenario.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Patients
            .AsNoTracking().SingleAsync(patient => patient.UserId == user.Id);
    }
}
