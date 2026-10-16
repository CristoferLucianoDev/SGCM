using System.Net;
using System.Net.Http.Json;
using Reqnroll;
using Shouldly;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Web;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Pasos de los escenarios de la recuperación de contraseña: solicitud del enlace y restablecimiento.
/// </summary>
[Binding]
public sealed class RecuperacionDeContrasenaSteps(ApiScenario scenario)
{
    private const string ForgotPasswordUrl = "/api/accounts/forgot-password";
    private const string ResetPasswordUrl = "/api/accounts/reset-password";
    private const string LoginUrl = "/api/auth/login";

    [When("solicito recuperar la contraseña de {string}")]
    public Task SolicitoRecuperar(string email) => ForgotPasswordAsync(email);

    [Given("que solicité recuperar la contraseña de {string}")]
    public async Task DadoQueSoliciteRecuperar(string email)
    {
        await ForgotPasswordAsync(email);
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.Accepted);
    }

    [Given("que solicité {int} veces recuperar la contraseña de {string}")]
    public async Task DadoQueSoliciteRecuperarVariasVeces(int requests, string email)
    {
        for (var request = 0; request < requests; request++)
        {
            await DadoQueSoliciteRecuperar(email);
        }
    }

    [Given("que inicié sesión {int} veces con el correo {string} y la contraseña {string}")]
    public async Task DadoQueInicieSesionVariasVeces(int logins, string email, string password)
    {
        for (var login = 0; login < logins; login++)
        {
            scenario.Response = await scenario.Client.PostAsJsonAsync(LoginUrl, new { email, password });
            await scenario.Response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        }
    }

    [When("restablezco la contraseña con el último enlace de {string} y la contraseña {string}")]
    public Task RestablezcoConElUltimoEnlace(string email, string newPassword) =>
        ResetPasswordAsync(ResetTokensFor(email)[^1], newPassword);

    [When("restablezco la contraseña con el primer enlace de {string} y la contraseña {string}")]
    public Task RestablezcoConElPrimerEnlace(string email, string newPassword) =>
        ResetPasswordAsync(ResetTokensFor(email)[0], newPassword);

    [Given("que restablecí la contraseña con el último enlace de {string} y la contraseña {string}")]
    public async Task DadoQueRestableci(string email, string newPassword)
    {
        await RestablezcoConElUltimoEnlace(email, newPassword);
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    [Then("la solicitud de recuperación es aceptada")]
    public async Task LaSolicitudEsAceptada()
    {
        var response = scenario.Response!;

        await response.ShouldHaveStatusAsync(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Then("{string} recibe un enlace de recuperación")]
    public void RecibeUnEnlace(string email) =>
        PasswordResetNotificationsFor(email)
            .ShouldHaveSingleItem()
            .ResetUrl.ShouldStartWith(SgcmApplicationFactory.PasswordResetUrl);

    [Then("{string} no recibe ningún enlace de recuperación")]
    public void NoRecibeNingunEnlace(string email) =>
        PasswordResetNotificationsFor(email).ShouldBeEmpty();

    [Then("la contraseña se restablece")]
    public Task LaContrasenaSeRestablece() =>
        scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

    [Then("el restablecimiento es rechazado con el error {string}")]
    [Then("la solicitud de recuperación es rechazada con el error {string}")]
    public async Task EsRechazadoConElError(string errorCode)
    {
        var response = scenario.Response!;

        await response.ShouldHaveStatusAsync(ExpectedStatus(errorCode));
        (await response.ReadErrorCodeAsync()).ShouldBe(errorCode);
    }

    [Then("todas las sesiones de {string} quedan cerradas")]
    public async Task TodasLasSesionesQuedanCerradas(string email)
    {
        var sessions = (await scenario.Factory.FindUserAsync(email))!.Sessions;
        var now = scenario.Factory.Clock.UtcNow;

        sessions.ShouldNotBeEmpty();
        sessions.ShouldAllBe(session =>
            !session.IsActiveAt(now) && session.RevocationReason == SessionRevocationReason.PasswordReset);
    }

    private async Task ForgotPasswordAsync(string email) =>
        scenario.Response = await scenario.Client.PostAsJsonAsync(ForgotPasswordUrl, new { email });

    private async Task ResetPasswordAsync(string token, string newPassword) =>
        scenario.Response = await scenario.Client.PostAsJsonAsync(ResetPasswordUrl, new { token, newPassword });

    // Sin esta aserción, un 202 sin correo haría fallar el paso con un índice fuera de rango
    // que no explica la causa.
    private IReadOnlyList<string> ResetTokensFor(string email)
    {
        var tokens = scenario.Factory.PasswordResetTokensFor(email);
        tokens.ShouldNotBeEmpty($"No se envió ningún enlace de recuperación a {email}.");

        return tokens;
    }

    private List<PasswordResetNotification> PasswordResetNotificationsFor(string email) =>
        scenario.Factory.Notifications.Sent
            .OfType<PasswordResetNotification>()
            .Where(notification => notification.Recipient.Email == email)
            .ToList();

    private static HttpStatusCode ExpectedStatus(string errorCode) => errorCode switch
    {
        _ when errorCode == UserErrors.TooManyPasswordResetRequests.Code
            || errorCode == CommonErrors.TooManyRequests.Code => HttpStatusCode.TooManyRequests,
        _ when errorCode == UserErrors.AccountInactive.Code => HttpStatusCode.Forbidden,
        _ => HttpStatusCode.BadRequest
    };
}
