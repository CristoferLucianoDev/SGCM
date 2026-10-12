using System.Net;
using System.Net.Http.Json;
using Reqnroll;
using Shouldly;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Web;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class ReenvioDeConfirmacionSteps(ApiScenario scenario)
{
    private const string ResendUrl = "/api/accounts/resend-confirmation";

    [Given("pasan {int} segundos")]
    public void PasanSegundos(int seconds) =>
        scenario.Factory.Clock.UtcNow += TimeSpan.FromSeconds(seconds);

    [Given("que pedí reenviar el correo de confirmación a {string}")]
    public async Task PediReenviar(string email)
    {
        await ResendAsync(email);
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [When("pido reenviar el correo de confirmación a {string}")]
    public Task PidoReenviar(string email) => ResendAsync(email);

    [Then("el reenvío es aceptado")]
    public void ElReenvioEsAceptado() =>
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.Accepted);

    [Then("el reenvío es rechazado con el error {string}")]
    public async Task ElReenvioEsRechazado(string errorCode)
    {
        scenario.Response!.StatusCode.ShouldBe(ExpectedStatus(errorCode));
        (await scenario.Response.ReadErrorCodeAsync()).ShouldBe(errorCode);
    }

    private async Task ResendAsync(string email) =>
        scenario.Response = await scenario.Client.PostAsJsonAsync(ResendUrl, new { email });

    private static HttpStatusCode ExpectedStatus(string errorCode) => errorCode switch
    {
        _ when errorCode == UserErrors.ResendTooSoon.Code
            || errorCode == UserErrors.EmailAlreadyConfirmed.Code => HttpStatusCode.Conflict,
        _ when errorCode == UserErrors.AccountInactive.Code => HttpStatusCode.Forbidden,
        _ => HttpStatusCode.BadRequest
    };
}
