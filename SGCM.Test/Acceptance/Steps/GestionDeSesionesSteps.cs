using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Reqnroll;
using Shouldly;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Identity;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class GestionDeSesionesSteps(ApiScenario scenario)
{
    private const string LoginUrl = "/api/auth/login";
    private const string RefreshUrl = "/api/auth/refresh";
    private const string LogoutUrl = "/api/auth/logout";

    // Separación entre inicios de sesión, para que el orden por LastUsedAt sea determinista.
    private static readonly TimeSpan BetweenLogins = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, DeviceTokens> _devices = new(StringComparer.Ordinal);
    private string? _email;
    private RefreshSessionResponse? _lastRefresh;

    [Given("que inicié sesión en el dispositivo {string} con el correo {string} y la contraseña {string}")]
    [When("inicio sesión en el dispositivo {string} con el correo {string} y la contraseña {string}")]
    public async Task InicioSesionEnElDispositivo(string device, string email, string password)
    {
        await SendAsync(LoginUrl, new LoginRequest(email, password));
        await Response.ShouldHaveStatusAsync(HttpStatusCode.OK);

        var login = await Response.ReadLoginAsync();
        _devices[device] = new DeviceTokens(login.RefreshToken!, null);
        _email = email;
    }

    [Given("que inicié sesión en {int} dispositivos con el correo {string} y la contraseña {string}")]
    public async Task InicioSesionEnVariosDispositivos(int count, string email, string password)
    {
        for (var device = 1; device <= count; device++)
        {
            await InicioSesionEnElDispositivo(device.ToString(CultureInfo.InvariantCulture), email, password);
            scenario.Factory.Clock.UtcNow += BetweenLogins;
        }
    }

    [When("renuevo la sesión del dispositivo {string}")]
    public async Task RenuevoLaSesion(string device)
    {
        var tokens = TokensOf(device);
        await SendAsync(RefreshUrl, new RefreshSessionRequest(tokens.Current));

        if (Response.StatusCode != HttpStatusCode.OK)
        {
            return;
        }

        _lastRefresh = await Response.Content.ReadFromJsonAsync<RefreshSessionResponse>()
            ?? throw new InvalidOperationException("La respuesta de la renovación vino vacía.");
        _devices[device] = new DeviceTokens(_lastRefresh.RefreshToken, tokens.Current);
    }

    [Given("que renové la sesión del dispositivo {string}")]
    public async Task DadoQueRenoveLaSesion(string device)
    {
        await RenuevoLaSesion(device);
        await Response.ShouldHaveStatusAsync(HttpStatusCode.OK);
    }

    [Given("que pasó el margen de gracia de la renovación")]
    public void DadoQuePasoElMargenDeGracia()
    {
        var graceSeconds = scenario.Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value.RefreshReuseGraceSeconds;
        scenario.Factory.Clock.UtcNow += TimeSpan.FromSeconds(graceSeconds + 1);
    }

    [When("presento el refresh token anterior del dispositivo {string}")]
    public Task PresentoElTokenAnterior(string device)
    {
        var previous = TokensOf(device).Previous
            ?? throw new InvalidOperationException($"El dispositivo {device} todavía no renovó su sesión.");

        return SendAsync(RefreshUrl, new RefreshSessionRequest(previous));
    }

    [When("cierro la sesión del dispositivo {string}")]
    public Task CierroLaSesion(string device) =>
        SendAsync(LogoutUrl, new LogoutRequest(TokensOf(device).Current));

    [Then("recibo un par de tokens nuevo para el dispositivo {string}")]
    public async Task ReciboUnParDeTokensNuevo(string device)
    {
        await Response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var tokens = TokensOf(device);
        _lastRefresh.ShouldNotBeNull();
        _lastRefresh.AccessToken.ShouldNotBeNullOrEmpty();
        _lastRefresh.RefreshToken.ShouldNotBe(tokens.Previous);

        var session = await FindSessionAsync(device);
        session.PreviousRefreshTokenHash.ShouldBe(scenario.Factory.HashRefreshToken(tokens.Previous!));
    }

    [Then("la renovación es rechazada")]
    public async Task LaRenovacionEsRechazada()
    {
        await Response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await Response.ReadErrorCodeAsync()).ShouldBe(UserErrors.SessionInvalid.Code);
    }

    [Then("el cierre de sesión es aceptado")]
    public Task ElCierreDeSesionEsAceptado() =>
        Response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

    [Then("la sesión del dispositivo {string} sigue activa")]
    public async Task LaSesionSigueActiva(string device)
    {
        var session = await FindSessionAsync(device);

        session.IsActiveAt(scenario.Factory.Clock.UtcNow).ShouldBeTrue();
        session.RevokedAt.ShouldBeNull();
    }

    [Then("la sesión del dispositivo {string} quedó revocada por {string}")]
    public async Task LaSesionQuedoRevocada(string device, string reason)
    {
        var session = await FindSessionAsync(device);

        session.RevocationReason.ShouldBe(ParseReason(reason));
        session.IsActiveAt(scenario.Factory.Clock.UtcNow).ShouldBeFalse();
    }

    [Then("todas las sesiones de {string} quedaron revocadas por {string}")]
    public async Task TodasLasSesionesQuedaronRevocadas(string email, string reason)
    {
        var expected = ParseReason(reason);
        var now = scenario.Factory.Clock.UtcNow;

        var sessions = (await FindUserAsync(email)).Sessions;

        sessions.ShouldNotBeEmpty();
        sessions.ShouldAllBe(session => session.RevocationReason == expected && !session.IsActiveAt(now));
    }

    [Then("{string} tiene {int} sesiones activas")]
    public async Task TieneSesionesActivas(string email, int count)
    {
        var now = scenario.Factory.Clock.UtcNow;

        (await FindUserAsync(email)).Sessions.Count(session => session.IsActiveAt(now)).ShouldBe(count);
    }

    private HttpResponseMessage Response =>
        scenario.Response ?? throw new InvalidOperationException("Todavía no se envió ninguna petición.");

    private async Task SendAsync<TRequest>(string url, TRequest request)
    {
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.PostAsJsonAsync(url, request);
    }

    private DeviceTokens TokensOf(string device) =>
        _devices.TryGetValue(device, out var tokens)
            ? tokens
            : throw new InvalidOperationException($"No se inició sesión en el dispositivo {device}.");

    // La sesión se identifica por el hash de su refresh token vigente.
    private Task<UserSession> FindSessionAsync(string device)
    {
        var email = _email ?? throw new InvalidOperationException("No se inició sesión en ningún dispositivo.");

        return scenario.Factory.FindSessionAsync(email, TokensOf(device).Current);
    }

    private async Task<User> FindUserAsync(string email) =>
        await scenario.Factory.FindUserAsync(email)
        ?? throw new InvalidOperationException($"No hay un usuario con el correo {email}.");

    private static SessionRevocationReason ParseReason(string reason) => reason switch
    {
        "cierre de sesión" => SessionRevocationReason.Logout,
        "reutilización del refresh token" => SessionRevocationReason.TokenReuse,
        "límite de sesiones" => SessionRevocationReason.SessionLimit,
        _ => throw new InvalidOperationException($"Motivo de revocación desconocido: {reason}.")
    };

    // Refresh token vigente del dispositivo y el anterior a su última renovación, si la hubo.
    private sealed record DeviceTokens(string Current, string? Previous);
}
