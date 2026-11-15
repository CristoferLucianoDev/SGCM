using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.RefreshSession;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Identity;
using Shouldly;

namespace SGCM.Test.Integration.Identity;

[Trait("Category", "Integration")]
public sealed class SessionManagementTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string RefreshUrl = "/api/auth/refresh";
    private const string LogoutUrl = "/api/auth/logout";
    private const string Email = "paciente@correo.com";
    private const string Password = "Secreta123";
    private const string UnknownToken = "token-desconocido";
    private const int TwoDevices = 2;

    // Lo justo para pasar un límite que se evalúa con desigualdad estricta.
    private static readonly TimeSpan JustPast = TimeSpan.FromSeconds(1);

    // Separación entre inicios de sesión, para que el orden por LastUsedAt sea determinista.
    private static readonly TimeSpan BetweenLogins = TimeSpan.FromMinutes(1);

    // Token más largo que el máximo que admite el validador.
    private static readonly string TooLongToken = new('t', RefreshSessionCommandValidator.MaxRefreshTokenLength + 1);

    public static TheoryData<string> InvalidTokens => new() { "", "   ", TooLongToken };

    private JwtOptions JwtOptions => Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

    private TimeSpan PastReuseGrace => TimeSpan.FromSeconds(JwtOptions.RefreshReuseGraceSeconds) + JustPast;

    [Fact]
    public async Task Renovar_con_un_refresh_token_valido_entrega_un_par_nuevo_y_rota_la_sesion()
    {
        var presented = await LoginPatientAsync();
        Clock.UtcNow += BetweenLogins;

        using var response = await RefreshAsync(presented);

        var refreshed = await ReadRefreshAsync(response);
        refreshed.AccessToken.ShouldNotBeNullOrEmpty();
        refreshed.RefreshToken.ShouldNotBe(presented);
        refreshed.RefreshTokenExpiresAt.ShouldBe(Clock.UtcNow.AddDays(JwtOptions.RefreshTokenDays));
        var session = await FindSessionAsync(refreshed.RefreshToken);
        session.PreviousRefreshTokenHash.ShouldBe(Factory.HashRefreshToken(presented));
        session.LastUsedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public async Task El_refresh_token_anterior_dentro_del_margen_responde_401_sin_revocar_la_sesion()
    {
        var presented = await LoginPatientAsync();
        var refreshed = await RefreshSuccessfullyAsync(presented);

        using (var previous = await RefreshAsync(presented))
            await ShouldBeSessionInvalidAsync(previous);

        await RefreshSuccessfullyAsync(refreshed.RefreshToken);
    }

    [Fact]
    public async Task El_refresh_token_anterior_justo_en_el_limite_del_margen_no_revoca_la_sesion()
    {
        var presented = await LoginPatientAsync();
        var refreshed = await RefreshSuccessfullyAsync(presented);
        Clock.UtcNow += TimeSpan.FromSeconds(JwtOptions.RefreshReuseGraceSeconds);

        using (var previous = await RefreshAsync(presented))
            await ShouldBeSessionInvalidAsync(previous);

        ShouldBeActive(await FindSessionAsync(refreshed.RefreshToken));
        await RefreshSuccessfullyAsync(refreshed.RefreshToken);
    }

    [Fact]
    public async Task Una_cuenta_desactivada_no_renueva_su_sesion()
    {
        var presented = await LoginPatientAsync();
        await Factory.UpdateUserAsync(Email, user => user.Deactivate().IsSuccess.ShouldBeTrue());

        using var response = await RefreshAsync(presented);

        await ShouldBeSessionInvalidAsync(response);
        // El trigger de la base revoca las sesiones al desactivar la cuenta (Modelo de Datos, sección 8).
        var session = await FindSessionAsync(presented);
        session.RevokedAt.ShouldNotBeNull();
        session.RevocationReason.ShouldBe(SessionRevocationReason.AccountDeactivated);
    }

    [Fact]
    public async Task Renovar_conserva_el_paso_pendiente_del_paciente_sin_perfil()
    {
        var presented = await LoginPatientAsync(withProfile: false);

        var refreshed = await RefreshSuccessfullyAsync(presented);

        refreshed.PendingStep.ShouldBe(PendingStepClaimValues.PatientProfile);
    }

    [Fact]
    public async Task Una_sesion_vencida_no_se_renueva()
    {
        var presented = await LoginPatientAsync();
        Clock.UtcNow += TimeSpan.FromDays(JwtOptions.RefreshTokenDays) + JustPast;

        using var response = await RefreshAsync(presented);

        await ShouldBeSessionInvalidAsync(response);
    }

    [Fact]
    public async Task Una_sesion_cerrada_no_se_renueva()
    {
        var presented = await LoginPatientAsync();
        using (var logout = await LogoutAsync(presented))
            await logout.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        using var response = await RefreshAsync(presented);

        await ShouldBeSessionInvalidAsync(response);
    }

    [Fact]
    public async Task Un_refresh_token_desconocido_responde_401()
    {
        await LoginPatientAsync();

        using var response = await RefreshAsync(UnknownToken);

        await ShouldBeSessionInvalidAsync(response);
    }

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task Renovar_con_un_token_vacio_o_demasiado_largo_responde_400(string token)
    {
        using var response = await RefreshAsync(token);

        await ShouldBeRefreshTokenRequiredAsync(response);
    }

    [Fact]
    public async Task Reutilizar_el_token_anterior_fuera_del_margen_revoca_todas_las_sesiones()
    {
        var (first, second) = await LoginPatientInTwoDevicesAsync();
        var refreshed = await RefreshSuccessfullyAsync(first);
        Clock.UtcNow += PastReuseGrace;

        using (var reused = await RefreshAsync(first))
            await ShouldBeSessionInvalidAsync(reused);

        var user = await FindPatientAsync();
        user.Sessions.ShouldNotBeEmpty();
        user.Sessions.ShouldAllBe(session => !session.IsActiveAt(Clock.UtcNow)
            && session.RevocationReason == SessionRevocationReason.TokenReuse);
        using (var firstRefresh = await RefreshAsync(refreshed.RefreshToken))
            await ShouldBeSessionInvalidAsync(firstRefresh);
        using (var secondRefresh = await RefreshAsync(second))
            await ShouldBeSessionInvalidAsync(secondRefresh);
    }

    [Fact]
    public async Task El_token_anterior_de_una_sesion_cerrada_no_revoca_las_demas()
    {
        var (first, second) = await LoginPatientInTwoDevicesAsync();
        var refreshed = await RefreshSuccessfullyAsync(first);
        using (var logout = await LogoutAsync(refreshed.RefreshToken))
            await logout.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        Clock.UtcNow += PastReuseGrace;

        using (var reused = await RefreshAsync(first))
            await ShouldBeSessionInvalidAsync(reused);

        ShouldBeActive(await FindSessionAsync(second));
        await RefreshSuccessfullyAsync(second);
    }

    [Fact]
    public async Task Cerrar_sesion_revoca_solo_esa_sesion()
    {
        var (first, second) = await LoginPatientInTwoDevicesAsync();

        using (var logout = await LogoutAsync(first))
            await logout.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        (await FindSessionAsync(first)).RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        ShouldBeActive(await FindSessionAsync(second));
        using (var firstRefresh = await RefreshAsync(first))
            await ShouldBeSessionInvalidAsync(firstRefresh);
        await RefreshSuccessfullyAsync(second);
    }

    [Fact]
    public async Task Cerrar_sesion_con_el_refresh_token_anterior_cierra_esa_sesion()
    {
        var (first, second) = await LoginPatientInTwoDevicesAsync();
        var refreshed = await RefreshSuccessfullyAsync(first);

        using (var logout = await LogoutAsync(first))
            await logout.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        (await FindSessionAsync(refreshed.RefreshToken)).RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        ShouldBeActive(await FindSessionAsync(second));
    }

    [Fact]
    public async Task Cerrar_sesion_es_idempotente()
    {
        var presented = await LoginPatientAsync();
        using (var logout = await LogoutAsync(presented))
            await logout.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        using (var again = await LogoutAsync(presented))
            await again.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        using (var unknown = await LogoutAsync(UnknownToken))
            await unknown.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task Cerrar_sesion_con_un_token_vacio_o_demasiado_largo_responde_400(string token)
    {
        using var response = await LogoutAsync(token);

        await ShouldBeRefreshTokenRequiredAsync(response);
    }

    [Fact]
    public async Task El_inicio_de_sesion_que_supera_el_maximo_revoca_la_sesion_usada_hace_mas_tiempo()
    {
        await CreatePatientAsync();
        var tokens = await LoginInDevicesAsync(JwtOptions.MaxActiveSessionsPerUser);
        var renewedFirst = await RefreshSuccessfullyAsync(tokens[0]);

        var newest = await LoginSuccessfullyAsync(Email, Password);

        var user = await FindPatientAsync();
        user.Sessions.Count(session => session.IsActiveAt(Clock.UtcNow)).ShouldBe(JwtOptions.MaxActiveSessionsPerUser);
        (await FindSessionAsync(tokens[1])).RevocationReason.ShouldBe(SessionRevocationReason.SessionLimit);
        ShouldBeActive(await FindSessionAsync(renewedFirst.RefreshToken));
        ShouldBeActive(await FindSessionAsync(newest.RefreshToken!));
    }

    [Fact]
    public async Task Con_el_maximo_exacto_de_sesiones_no_se_revoca_ninguna()
    {
        await CreatePatientAsync();

        await LoginInDevicesAsync(JwtOptions.MaxActiveSessionsPerUser);

        var user = await FindPatientAsync();
        user.Sessions.Count.ShouldBe(JwtOptions.MaxActiveSessionsPerUser);
        user.Sessions.ShouldAllBe(session => session.IsActiveAt(Clock.UtcNow) && session.RevokedAt == null);
    }

    [Fact]
    public async Task Una_sesion_cerrada_no_cuenta_para_el_limite()
    {
        await CreatePatientAsync();
        var tokens = await LoginInDevicesAsync(JwtOptions.MaxActiveSessionsPerUser);
        // Se cierra la usada más recientemente: si el límite contara las revocadas, el inicio
        // nuevo revocaría la primera, y la prueba lo detecta.
        using (var logout = await LogoutAsync(tokens[^1]))
            await logout.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        await LoginSuccessfullyAsync(Email, Password);

        ShouldBeActive(await FindSessionAsync(tokens[0]));
        var user = await FindPatientAsync();
        user.Sessions.ShouldNotContain(session => session.RevocationReason == SessionRevocationReason.SessionLimit);
        user.Sessions.Count(session => session.IsActiveAt(Clock.UtcNow)).ShouldBe(JwtOptions.MaxActiveSessionsPerUser);
    }

    // Paciente confirmado con una sesión; devuelve su refresh token.
    private async Task<string> LoginPatientAsync(bool withProfile = true)
    {
        await CreatePatientAsync(withProfile);

        var login = await LoginSuccessfullyAsync(Email, Password);
        return login.RefreshToken!;
    }

    private async Task<(string First, string Second)> LoginPatientInTwoDevicesAsync()
    {
        await CreatePatientAsync();

        var tokens = await LoginInDevicesAsync(TwoDevices);
        return (tokens[0], tokens[1]);
    }

    private async Task CreatePatientAsync(bool withProfile = true)
    {
        await CreateConfirmedPatientAsync(Email, Password);

        if (withProfile)
        {
            await Factory.InsertPatientProfileAsync(Email);
        }
    }

    // Adelanta el reloj después de cada inicio, así el primero es el usado hace más tiempo.
    private async Task<List<string>> LoginInDevicesAsync(int count)
    {
        var tokens = new List<string>();

        for (var device = 0; device < count; device++)
        {
            var login = await LoginSuccessfullyAsync(Email, Password);
            tokens.Add(login.RefreshToken!);
            Clock.UtcNow += BetweenLogins;
        }

        return tokens;
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        Client.PostAsJsonAsync(RefreshUrl, new RefreshSessionRequest(refreshToken));

    private Task<HttpResponseMessage> LogoutAsync(string refreshToken) =>
        Client.PostAsJsonAsync(LogoutUrl, new LogoutRequest(refreshToken));

    private async Task<RefreshSessionResponse> RefreshSuccessfullyAsync(string refreshToken)
    {
        using var response = await RefreshAsync(refreshToken);

        return await ReadRefreshAsync(response);
    }

    private static async Task<RefreshSessionResponse> ReadRefreshAsync(HttpResponseMessage response)
    {
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<RefreshSessionResponse>()
            ?? throw new InvalidOperationException("La respuesta de la renovación vino vacía.");
    }

    private static async Task ShouldBeSessionInvalidAsync(HttpResponseMessage response)
    {
        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.SessionInvalid.Code);
    }

    private static async Task ShouldBeRefreshTokenRequiredAsync(HttpResponseMessage response)
    {
        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.RefreshTokenRequired.Code);
    }

    private void ShouldBeActive(UserSession session)
    {
        session.IsActiveAt(Clock.UtcNow).ShouldBeTrue();
        session.RevokedAt.ShouldBeNull();
    }

    private async Task<User> FindPatientAsync() =>
        await FindUserAsync(Email) ?? throw new InvalidOperationException($"No hay un usuario con el correo {Email}.");

    private Task<UserSession> FindSessionAsync(string refreshToken) =>
        Factory.FindSessionAsync(Email, refreshToken);
}
