using System.Net;
using System.Net.Http.Json;
using System.Text;
using Shouldly;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Jobs;

namespace SGCM.Test.Integration.Identity;

[Trait("Category", "Integration")]
public sealed class ResendConfirmationEmailTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string ResendUrl = "/api/accounts/resend-confirmation";
    private const string RegisterUrl = "/api/accounts/register";
    private const string ConfirmUrl = "/api/accounts/confirm-email";
    private const string Email = "paciente@correo.com";
    private const string ExpiredEmail = "vencido@correo.com";
    private const string Password = "Secreta123";

    // Espera mínima entre envíos y vigencia del enlace de confirmación según el PBI.
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(15);

    [Fact]
    public async Task Reenvio_tras_la_espera_envia_un_enlace_nuevo()
    {
        await RegisterAsync(Email);
        Clock.UtcNow += ResendCooldown;

        var response = await ResendAsync(Email);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var tokens = Factory.ConfirmationTokensFor(Email);
        tokens.Count.ShouldBe(2);
        tokens[0].ShouldNotBe(tokens[1]);

        var user = await FindUserAsync(Email);
        user!.Tokens.Count.ShouldBe(2);
        user.Tokens.Count(token => token.InvalidatedAt is null).ShouldBe(1);
    }

    [Fact]
    public async Task Reenvio_invalida_el_enlace_anterior()
    {
        await RegisterAsync(Email);
        Clock.UtcNow += ResendCooldown;
        (await ResendAsync(Email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var tokens = Factory.ConfirmationTokensFor(Email);

        var previous = await ConfirmAsync(tokens[0]);

        previous.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await previous.ReadErrorCodeAsync()).ShouldBe(UserErrors.TokenInvalid.Code);
        (await FindUserAsync(Email))!.IsEmailConfirmed.ShouldBeFalse();

        var latest = await ConfirmAsync(tokens[^1]);

        latest.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task El_enlace_reenviado_es_valido_hasta_un_segundo_antes_de_15_minutos()
    {
        await RegisterAsync(Email);
        Clock.UtcNow += ResendCooldown;
        (await ResendAsync(Email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Clock.UtcNow += ConfirmationLifetime - TimeSpan.FromSeconds(1);

        var response = await ConfirmAsync(Factory.ConfirmationTokensFor(Email)[^1]);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task El_enlace_reenviado_vence_a_los_15_minutos()
    {
        await RegisterAsync(Email);
        Clock.UtcNow += ResendCooldown;
        (await ResendAsync(Email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Clock.UtcNow += ConfirmationLifetime;

        var response = await ConfirmAsync(Factory.ConfirmationTokensFor(Email)[^1]);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.RegistrationExpired.Code);
    }

    [Fact]
    public async Task Reenvio_antes_de_la_espera_se_rechaza()
    {
        await RegisterAsync(Email);
        Clock.UtcNow += ResendCooldown - TimeSpan.FromSeconds(1);

        var response = await ResendAsync(Email);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.ResendTooSoon.Code);
        Factory.ConfirmationTokensFor(Email).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Reenvio_con_el_ultimo_enlace_vencido_indica_registrarse_de_nuevo()
    {
        await RegisterAsync(Email);
        Clock.UtcNow += ConfirmationLifetime;

        var response = await ResendAsync(Email);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.RegistrationExpired.Code);
        Factory.ConfirmationTokensFor(Email).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Reenvio_de_una_cuenta_confirmada_se_rechaza()
    {
        await RegisterAsync(Email);
        (await ConfirmAsync(Factory.ConfirmationTokensFor(Email).Single())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Clock.UtcNow += TimeSpan.FromMinutes(1);

        var response = await ResendAsync(Email);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.EmailAlreadyConfirmed.Code);
    }

    [Fact]
    public async Task Reenvio_de_una_cuenta_inactiva_se_rechaza()
    {
        // El enlace del usuario sigue vigente porque el reloj de la app empieza en UserMother.Now.
        var user = UserMother.Pending();
        user.Deactivate();
        await SaveAsync(user);
        Clock.UtcNow += TimeSpan.FromMinutes(1);

        var response = await ResendAsync(UserMother.DefaultEmail.Value);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.AccountInactive.Code);
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reenvio_de_un_correo_sin_registro_indica_registrarse_de_nuevo()
    {
        var response = await ResendAsync(Email);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.RegistrationExpired.Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"email\":\"\"}")]
    [InlineData("{\"email\":\"no-es-un-correo\"}")]
    public async Task Reenvio_con_correo_invalido_se_rechaza(string body)
    {
        var response = await Client.PostAsync(ResendUrl, new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(CommonErrors.InvalidEmail.Code);
    }

    [Fact]
    public async Task La_limpieza_conserva_la_cuenta_que_recibio_un_enlace_nuevo()
    {
        // La versión concurrente de este caso, un reenvío en curso mientras corre la limpieza,
        // la cubre DeleteExpiredPendingRegistrationsAsync_salta_el_usuario_bloqueado_por_otra_transaccion
        // en UserRepositoryTest.cs.
        await RegisterAsync(ExpiredEmail);
        await RegisterAsync(Email);
        Clock.UtcNow += TimeSpan.FromMinutes(14);
        (await ResendAsync(Email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Clock.UtcNow += TimeSpan.FromMinutes(2);

        await Factory.GetJob<PurgeUnconfirmedAccountsJob>().RunOnceAsync(CancellationToken.None);

        // Prueba que la limpieza corrió: RunOnceAsync registra los fallos sin propagarlos.
        (await FindUserAsync(ExpiredEmail)).ShouldBeNull();
        (await FindUserAsync(Email)).ShouldNotBeNull();

        var response = await ConfirmAsync(Factory.ConfirmationTokensFor(Email)[^1]);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task RegisterAsync(string email)
    {
        var response = await Client.PostAsJsonAsync(RegisterUrl, new { email, password = Password });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    private Task<HttpResponseMessage> ResendAsync(string email) =>
        Client.PostAsJsonAsync(ResendUrl, new { email });

    private Task<HttpResponseMessage> ConfirmAsync(string token) =>
        Client.PostAsJsonAsync(ConfirmUrl, new { token });
}
