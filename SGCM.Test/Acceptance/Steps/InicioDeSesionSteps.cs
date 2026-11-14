using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Reqnroll;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Common;
using SGCM.Web.Contracts.Identity;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class InicioDeSesionSteps(ApiScenario scenario)
{
    private const string LoginUrl = "/api/auth/login";
    private const string RegisterUrl = "/api/accounts/register";
    private const string ConfirmUrl = "/api/accounts/confirm-email";
    private const string WrongPassword = "ClaveIncorrecta999";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Given("que existe un paciente confirmado con el correo {string} y la contraseña {string}")]
    public async Task DadoUnPacienteConfirmado(string email, string password)
    {
        var register = await scenario.Client.PostAsJsonAsync(RegisterUrl, new { email, password });
        register.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var token = scenario.Factory.ConfirmationTokensFor(email)[^1];
        var confirm = await scenario.Client.PostAsJsonAsync(ConfirmUrl, new { token });
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Given("que la cuenta de {string} está desactivada")]
    public Task DadoUnaCuentaDesactivada(string email) =>
        scenario.Factory.UpdateUserAsync(email, user => user.Deactivate().IsSuccess.ShouldBeTrue());

    [Given("que {string} tiene el segundo factor activo")]
    public Task DadoElSegundoFactorActivo(string email) =>
        scenario.Factory.EnableTwoFactorAsync(email);

    [Given("que {string} completó su perfil de paciente")]
    public Task DadoUnPerfilDePaciente(string email) =>
        scenario.Factory.InsertPatientProfileAsync(email);

    [Given("que existe un médico con contraseña temporal con el correo {string} y la contraseña {string}")]
    public Task DadoUnMedicoConContrasenaTemporal(string email, string password) =>
        SaveDoctorAsync(email, password, mustChangePassword: true);

    [Given("que existe un médico con el correo {string} y la contraseña {string}")]
    public Task DadoUnMedico(string email, string password) =>
        SaveDoctorAsync(email, password, mustChangePassword: false);

    [Given("que fallé {int} veces el inicio de sesión con el correo {string}")]
    public async Task DadoIntentosFallidos(int attempts, string email)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            await LoginAsync(email, WrongPassword);
            scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [When("inicio sesión con el correo {string} y la contraseña {string}")]
    public Task InicioSesion(string email, string password) => LoginAsync(email, password);

    [Then("el inicio de sesión es aceptado y recibo los tokens")]
    public async Task ElInicioDeSesionEsAceptado()
    {
        var login = await ReadSuccessfulLoginAsync();

        login.RequiresTwoFactor.ShouldBeFalse();
        login.AccessToken.ShouldNotBeNullOrEmpty();
        login.RefreshToken.ShouldNotBeNullOrEmpty();
    }

    [Then("el inicio de sesión es rechazado con el error {string}")]
    public async Task ElInicioDeSesionEsRechazado(string errorCode)
    {
        scenario.Response!.StatusCode.ShouldBe(ExpectedStatus(errorCode));
        string.Equals(await scenario.Response.ReadErrorCodeAsync(), errorCode, StringComparison.Ordinal)
            .ShouldBeTrue($"Se esperaba el código de error {errorCode}.");
    }

    [Then("se pide el segundo factor sin entregar tokens")]
    public async Task SePideElSegundoFactor()
    {
        var login = await ReadSuccessfulLoginAsync();

        login.RequiresTwoFactor.ShouldBeTrue();
        login.AccessToken.ShouldBeNull();
        login.RefreshToken.ShouldBeNull();
    }

    [Then("el paso pendiente es {string}")]
    public async Task ElPasoPendienteEs(string pendingStep)
    {
        var login = await ReadSuccessfulLoginAsync();

        login.PendingStep.ShouldBe(pendingStep);
        ReadAccessToken(login).GetClaim(AppClaims.PendingStep).Value.ShouldBe(pendingStep);
    }

    [Then("no hay paso pendiente")]
    public async Task NoHayPasoPendiente()
    {
        var login = await ReadSuccessfulLoginAsync();

        login.PendingStep.ShouldBeNull();
        ReadAccessToken(login).TryGetClaim(AppClaims.PendingStep, out _).ShouldBeFalse();
    }

    private Task SaveDoctorAsync(string email, string password, bool mustChangePassword) =>
        scenario.Factory.SaveAsync(UserMother.WithRole(
            UserRole.Doctor,
            mustChangePassword,
            Email.Create(email).Value,
            scenario.Factory.HashPassword(password)));

    internal async Task LoginAsync(string email, string password, string? clientType = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginUrl)
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        if (clientType is not null)
            request.Headers.Add(ClientTypeHeader.Name, clientType);

        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.SendAsync(request);
    }

    internal async Task<LoginResponse> ReadSuccessfulLoginAsync()
    {
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await scenario.Response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<LoginResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("La respuesta de autenticación vino vacía.");
    }

    internal static JsonWebToken ReadAccessToken(LoginResponse login) =>
        new JsonWebTokenHandler().ReadJsonWebToken(
            login.AccessToken ?? throw new InvalidOperationException("La respuesta no trae access token."));

    private static HttpStatusCode ExpectedStatus(string errorCode) => errorCode switch
    {
        _ when errorCode == UserErrors.InvalidCredentials.Code => HttpStatusCode.Unauthorized,
        _ when errorCode == UserErrors.TooManyLoginAttempts.Code
            || errorCode == CommonErrors.TooManyRequests.Code => HttpStatusCode.TooManyRequests,
        _ => HttpStatusCode.BadRequest
    };
}
