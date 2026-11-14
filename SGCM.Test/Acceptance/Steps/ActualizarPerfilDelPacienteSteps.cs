using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Pasos de la actualización del perfil. El inicio de sesión, los proveedores y el perfil
/// completado vienen de <see cref="CompletarPerfilDelPacienteSteps"/>, que comparte el escenario.
/// </summary>
[Binding]
public sealed class ActualizarPerfilDelPacienteSteps(ApiScenario scenario, CompletarPerfilDelPacienteSteps perfil)
{
    private const string Url = "/api/patients/me";
    private const string ChangePasswordUrl = "/api/auth/change-password";
    private const string LoginUrl = "/api/auth/login";
    private const string RefreshUrl = "/api/auth/refresh";

    private string? _otherDeviceRefreshToken;

    [Given("que también inicié sesión en otro dispositivo con la contraseña {string}")]
    public async Task DadoQueTambienInicieSesionEnOtroDispositivo(string password)
    {
        using var response = await scenario.Client.PostAsJsonAsync(LoginUrl, new { email = perfil.Email, password });
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        _otherDeviceRefreshToken = (await response.ReadLoginAsync()).RefreshToken;
    }

    [When("cambio mi contraseña de {string} a {string}")]
    public async Task CambioMiContrasena(string currentPassword, string newPassword)
    {
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.PostAsJsonAsync(
            ChangePasswordUrl, new ChangePasswordRequest(currentPassword, newPassword));
    }

    [Then("el cambio de contraseña es aceptado")]
    public Task ElCambioDeContrasenaEsAceptado() =>
        scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

    [Then("el cambio de contraseña es rechazado con el error {string}")]
    public async Task ElCambioDeContrasenaEsRechazado(string errorCode)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await scenario.Response!.ReadErrorCodeAsync()).ShouldBe(errorCode);
    }

    [Then("mi sesión actual sigue activa")]
    public Task MiSesionActualSigueActiva() => AssertRefreshAsync(perfil.CurrentRefreshToken, HttpStatusCode.OK);

    [Then("la sesión del otro dispositivo fue revocada")]
    public Task LaSesionDelOtroDispositivoFueRevocada() =>
        AssertRefreshAsync(_otherDeviceRefreshToken, HttpStatusCode.Unauthorized);

    [Then("la sesión del otro dispositivo sigue activa")]
    public Task LaSesionDelOtroDispositivoSigueActiva() =>
        AssertRefreshAsync(_otherDeviceRefreshToken, HttpStatusCode.OK);

    [Then("puedo iniciar sesión con la contraseña {string}")]
    public Task PuedoIniciarSesion(string password) => AssertLoginAsync(password, HttpStatusCode.OK);

    [Then("no puedo iniciar sesión con la contraseña {string}")]
    public Task NoPuedoIniciarSesion(string password) => AssertLoginAsync(password, HttpStatusCode.Unauthorized);

    [When("actualizo mi contacto conservando el proveedor {string}")]
    public Task ActualizoMiContactoConservandoElProveedor(string providerName) =>
        SendAsync(UpdatePatientProfileRequestMother.WithAllData(perfil.InsuranceProviderIdOf(providerName)));

    [When("actualizo mi perfil con el proveedor {string}")]
    public Task ActualizoMiPerfilConElProveedor(string providerName) =>
        SendAsync(UpdatePatientProfileRequestMother.WithAllData(perfil.InsuranceProviderIdOf(providerName)));

    [When("actualizo mi perfil con el proveedor {string} y sin número de póliza")]
    public Task ActualizoMiPerfilSinPoliza(string providerName) =>
        SendAsync(UpdatePatientProfileRequestMother.WithAllData(perfil.InsuranceProviderIdOf(providerName)) with
        {
            PolicyNumber = null
        });

    [When("quito mi seguro")]
    public async Task QuitoMiSeguro()
    {
        // Envía los datos de contacto actuales sin proveedor, para quitar solo el seguro.
        var current = await perfil.FindMyPatientAsync();

        await SendAsync(new UpdatePatientProfileRequest(
            current.PhoneNumber,
            current.Address,
            current.EmergencyContact?.Name,
            current.EmergencyContact?.PhoneNumber,
            InsuranceProviderId: null,
            PolicyNumber: null));
    }

    [When(@"^actualizo mi perfil con (el teléfono vacío|un teléfono de 21 caracteres|una dirección de 251 caracteres|un contacto de emergencia sin teléfono)$")]
    public Task ActualizoMiPerfilConUnDatoInvalido(string dato)
    {
        var valid = UpdatePatientProfileRequestMother.WithAllData(perfil.InsuranceProviderIdOf("Seguros Universal"));

        return SendAsync(dato switch
        {
            "el teléfono vacío" => valid with { PhoneNumber = "" },
            "un teléfono de 21 caracteres" => valid with { PhoneNumber = new string('1', 21) },
            "una dirección de 251 caracteres" => valid with { Address = new string('A', 251) },
            _ => valid with { EmergencyContactPhone = null }
        });
    }

    [When("intento cambiar mi nombre, mi apellido y mi fecha de nacimiento")]
    public async Task IntentoCambiarMiIdentidad()
    {
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.PutAsJsonAsync(Url, new
        {
            firstName = "Otro",
            lastName = "Nombre",
            birthDate = PatientMother.DefaultBirthDate.AddYears(-10),
            phoneNumber = UpdatePatientProfileRequestMother.NewPhoneNumber
        });
    }

    [Then("la actualización es aceptada")]
    public Task LaActualizacionEsAceptada() => scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

    [Then("la actualización es rechazada con el error {string}")]
    public async Task LaActualizacionEsRechazada(string errorCode)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await scenario.Response!.ReadErrorCodeAsync()).ShouldBe(errorCode);
    }

    [Then("mi perfil tiene los datos de contacto nuevos")]
    public async Task MiPerfilTieneLosDatosDeContactoNuevos()
    {
        var patient = await perfil.FindMyPatientAsync();

        patient.PhoneNumber.ShouldBe(UpdatePatientProfileRequestMother.NewPhoneNumber);
        patient.Address.ShouldBe(UpdatePatientProfileRequestMother.NewAddress);
        patient.EmergencyContact.ShouldNotBeNull();
        patient.EmergencyContact.Name.ShouldBe(UpdatePatientProfileRequestMother.NewEmergencyContactName);
        patient.EmergencyContact.PhoneNumber.ShouldBe(UpdatePatientProfileRequestMother.NewEmergencyContactPhone);
    }

    [Then("mi perfil queda con el proveedor {string} y la póliza nueva")]
    public async Task MiPerfilQuedaConElProveedor(string providerName)
    {
        var patient = await perfil.FindMyPatientAsync();

        patient.InsuranceProviderId.ShouldBe(perfil.InsuranceProviderIdOf(providerName));
        patient.PolicyNumber.ShouldBe(UpdatePatientProfileRequestMother.NewPolicyNumber);
    }

    [Then("mi perfil queda sin proveedor ni póliza y conserva mis datos de contacto")]
    public async Task MiPerfilQuedaSinSeguro()
    {
        var patient = await perfil.FindMyPatientAsync();

        patient.InsuranceProviderId.ShouldBeNull();
        patient.PolicyNumber.ShouldBeNull();
        patient.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        patient.Address.ShouldBe(PatientMother.DefaultAddress);
        patient.EmergencyContact.ShouldNotBeNull();
        patient.EmergencyContact.Name.ShouldBe(PatientMother.DefaultEmergencyContactName);
    }

    [Then("mi perfil no cambió")]
    public async Task MiPerfilNoCambio()
    {
        var patient = await perfil.FindMyPatientAsync();

        patient.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        patient.Address.ShouldBe(PatientMother.DefaultAddress);
        patient.EmergencyContact.ShouldNotBeNull();
        patient.EmergencyContact.Name.ShouldBe(PatientMother.DefaultEmergencyContactName);
        patient.InsuranceProviderId.ShouldBe(perfil.InsuranceProviderIdOf("Seguros Universal"));
        patient.PolicyNumber.ShouldBe(PatientMother.DefaultPolicyNumber);
    }

    [Then("mi nombre, mi apellido y mi fecha de nacimiento no cambiaron")]
    public async Task MiIdentidadNoCambio()
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        var patient = await perfil.FindMyPatientAsync();

        patient.FirstName.ShouldBe(PatientMother.DefaultFirstName);
        patient.LastName.ShouldBe(PatientMother.DefaultLastName);
        patient.BirthDate.ShouldBe(PatientMother.DefaultBirthDate);
    }

    [Then("el perfil de {string} no cambió")]
    public async Task ElPerfilDeOtroNoCambio(string email)
    {
        var user = await scenario.Factory.FindUserAsync(email)
            ?? throw new InvalidOperationException($"No hay un usuario con el correo {email}.");
        using var scope = scenario.Factory.Services.CreateScope();
        var other = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Patients
            .AsNoTracking().SingleAsync(patient => patient.UserId == user.Id);

        other.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        other.Address.ShouldBeNull();
    }

    private async Task AssertRefreshAsync(string? refreshToken, HttpStatusCode expected)
    {
        refreshToken.ShouldNotBeNullOrWhiteSpace();
        using var response = await scenario.Client.PostAsJsonAsync(RefreshUrl, new RefreshSessionRequest(refreshToken!));
        await response.ShouldHaveStatusAsync(expected);
    }

    private async Task AssertLoginAsync(string password, HttpStatusCode expected)
    {
        using var response = await scenario.Client.PostAsJsonAsync(LoginUrl, new { email = perfil.Email, password });
        await response.ShouldHaveStatusAsync(expected);
    }

    private async Task SendAsync(UpdatePatientProfileRequest request)
    {
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.PutAsJsonAsync(Url, request);
    }
}
