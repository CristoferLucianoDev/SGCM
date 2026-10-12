using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Test.Common.Web;
using SGCM.Web.Jobs;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class ConfirmacionDeCuentaSteps(ApiScenario scenario)
{
    private const string ConfirmUrl = "/api/accounts/confirm-email";

    [Given("pasan {int} minutos")]
    public void PasanMinutos(int minutes) =>
        scenario.Factory.Clock.UtcNow += TimeSpan.FromMinutes(minutes);

    [Given("que abrí el último enlace de confirmación de {string}")]
    public async Task AbriElUltimoEnlace(string email)
    {
        await ConfirmAsync(scenario.Factory.ConfirmationTokensFor(email)[^1]);
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [When("abro el último enlace de confirmación de {string}")]
    public Task AbroElUltimoEnlace(string email) =>
        ConfirmAsync(scenario.Factory.ConfirmationTokensFor(email)[^1]);

    [When("abro el primer enlace de confirmación de {string}")]
    public Task AbroElPrimerEnlace(string email) =>
        ConfirmAsync(scenario.Factory.ConfirmationTokensFor(email)[0]);

    [When("se ejecuta la limpieza de registros pendientes")]
    public Task SeEjecutaLaLimpieza() =>
        scenario.Factory.GetJob<PurgeUnconfirmedAccountsJob>().RunOnceAsync(CancellationToken.None);

    [Then("la confirmación es aceptada")]
    public void LaConfirmacionEsAceptada() =>
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.NoContent);

    [Then("la confirmación es rechazada con el error {string}")]
    public async Task LaConfirmacionEsRechazada(string errorCode)
    {
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await scenario.Response.ReadErrorCodeAsync()).ShouldBe(errorCode);
    }

    [Then("la cuenta de {string} está confirmada")]
    public async Task LaCuentaEstaConfirmada(string email) =>
        (await scenario.Factory.FindUserAsync(email))!.IsEmailConfirmed.ShouldBeTrue();

    [Then("la cuenta de {string} sigue sin confirmar")]
    public async Task LaCuentaSigueSinConfirmar(string email) =>
        (await scenario.Factory.FindUserAsync(email))!.IsEmailConfirmed.ShouldBeFalse();

    [Then("el último enlace de confirmación de {string} queda marcado como usado")]
    public async Task ElUltimoEnlaceQuedaUsado(string email)
    {
        var tokenHash = HashOf(scenario.Factory.ConfirmationTokensFor(email)[^1]);
        var user = await scenario.Factory.FindUserAsync(email);

        user!.Tokens.Single(token => token.TokenHash == tokenHash).UsedAt.ShouldNotBeNull();
    }

    [Then("no existe una cuenta para {string}")]
    public async Task NoExisteUnaCuenta(string email) =>
        (await scenario.Factory.FindUserAsync(email)).ShouldBeNull();

    private async Task ConfirmAsync(string token) =>
        scenario.Response = await scenario.Client.PostAsJsonAsync(ConfirmUrl, new { token });

    private string HashOf(string token)
    {
        using var scope = scenario.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISecureTokenGenerator>().Hash(token);
    }
}
