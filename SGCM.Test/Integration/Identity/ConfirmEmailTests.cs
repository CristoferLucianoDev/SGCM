using System.Net;
using System.Net.Http.Json;
using System.Text;
using Shouldly;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;

namespace SGCM.Test.Integration.Identity;

[Trait("Category", "Integration")]
public sealed class ConfirmEmailTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string ConfirmUrl = "/api/accounts/confirm-email";
    private const string RegisterUrl = "/api/accounts/register";
    private const string Email = "paciente@correo.com";
    private const string Password = "Secreta123";
    private const int MaxTokenLength = 256;

    // Vigencia del enlace de confirmación según el PBI.
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(15);

    [Fact]
    public async Task Confirmar_con_enlace_vigente_confirma_la_cuenta_y_marca_el_enlace_como_usado()
    {
        await RegisterAsync();

        var response = await ConfirmAsync(Factory.ConfirmationTokensFor(Email).Single());

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var user = await FindUserAsync(Email);
        user!.IsEmailConfirmed.ShouldBeTrue();
        user.Tokens.ShouldHaveSingleItem().UsedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public async Task Confirmar_un_segundo_antes_del_vencimiento_confirma_la_cuenta()
    {
        await RegisterAsync();
        Clock.UtcNow += ConfirmationLifetime - TimeSpan.FromSeconds(1);

        var response = await ConfirmAsync(Factory.ConfirmationTokensFor(Email).Single());

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Confirmar_con_enlace_vencido_indica_que_debe_registrarse_de_nuevo()
    {
        await RegisterAsync();
        Clock.UtcNow += ConfirmationLifetime;

        var response = await ConfirmAsync(Factory.ConfirmationTokensFor(Email).Single());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.RegistrationExpired.Code);

        var user = await FindUserAsync(Email);
        user!.IsEmailConfirmed.ShouldBeFalse();
        user.Tokens.ShouldHaveSingleItem().UsedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Confirmar_con_enlace_ya_usado_se_rechaza()
    {
        await RegisterAsync();
        var token = Factory.ConfirmationTokensFor(Email).Single();
        await ConfirmAsync(token);

        var response = await ConfirmAsync(token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.TokenInvalid.Code);
    }

    [Fact]
    public async Task Confirmar_con_enlace_reemplazado_por_un_nuevo_registro_se_rechaza()
    {
        await RegisterAsync();
        await RegisterAsync();
        var tokens = Factory.ConfirmationTokensFor(Email);

        var replaced = await ConfirmAsync(tokens[0]);

        replaced.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await replaced.ReadErrorCodeAsync()).ShouldBe(UserErrors.TokenInvalid.Code);
        (await FindUserAsync(Email))!.IsEmailConfirmed.ShouldBeFalse();

        var latest = await ConfirmAsync(tokens[^1]);

        latest.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Confirmar_con_token_desconocido_se_rechaza()
    {
        var response = await ConfirmAsync("token-inventado");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.TokenInvalid.Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"token\":\"\"}")]
    [InlineData("{\"token\":\"   \"}")]
    public async Task Confirmar_sin_token_se_rechaza(string body)
    {
        var response = await Client.PostAsync(ConfirmUrl, new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.TokenInvalid.Code);
    }

    [Fact]
    public async Task Confirmar_con_token_de_mas_de_256_caracteres_se_rechaza()
    {
        var response = await ConfirmAsync(new string('a', MaxTokenLength + 1));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.TokenInvalid.Code);
    }

    private async Task RegisterAsync()
    {
        var response = await Client.PostAsJsonAsync(RegisterUrl, new { email = Email, password = Password });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    private Task<HttpResponseMessage> ConfirmAsync(string token) =>
        Client.PostAsJsonAsync(ConfirmUrl, new { token });
}
