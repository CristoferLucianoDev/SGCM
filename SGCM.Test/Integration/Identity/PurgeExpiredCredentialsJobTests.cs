using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Identity;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Jobs;
using Shouldly;

namespace SGCM.Test.Integration.Identity;

[Trait("Category", "Integration")]
public sealed class PurgeExpiredCredentialsJobTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string LogoutUrl = "/api/auth/logout";
    private const string Email = "paciente@correo.com";
    private const string Password = "Secreta123";
    private const string NewPassword = "NuevaClave456";
    private const string PendingEmail = "pendiente@correo.com";

    private TimeSpan PastRetention =>
        TimeSpan.FromDays(Factory.Services.GetRequiredService<IOptions<AccountsOptions>>().Value.CredentialRetentionDays + 1);

    [Fact]
    public async Task Limpieza_borra_la_sesion_cerrada_hace_mas_de_la_retencion_y_conserva_las_demas()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        var deviceA = await LoginSuccessfullyAsync(Email, Password);
        var deviceB = await LoginSuccessfullyAsync(Email, Password);
        await LogoutAsync(deviceA.RefreshToken!);
        Clock.UtcNow += PastRetention;
        var deviceC = await LoginSuccessfullyAsync(Email, Password);

        await RunPurgeAsync();

        // Prueba que la limpieza corrió: RunOnceAsync registra los fallos sin propagarlos.
        var hashA = Factory.HashRefreshToken(deviceA.RefreshToken!);
        (await FindPatientAsync(Email)).Sessions.ShouldNotContain(session => session.RefreshTokenHash == hashA);
        (await Factory.FindSessionAsync(Email, deviceB.RefreshToken!)).IsActiveAt(Clock.UtcNow).ShouldBeFalse();
        (await Factory.FindSessionAsync(Email, deviceC.RefreshToken!)).IsActiveAt(Clock.UtcNow).ShouldBeTrue();
    }

    [Fact]
    public async Task Limpieza_borra_los_tokens_usados_hace_mas_de_la_retencion_y_conserva_el_vigente()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        await RequestPasswordResetAsync();
        using (var reset = await ResetPasswordAsync(Factory.PasswordResetTokensFor(Email)[^1], NewPassword))
            await reset.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        Clock.UtcNow += PastRetention;
        await RequestPasswordResetAsync();

        await RunPurgeAsync();

        var token = (await FindPatientAsync(Email)).Tokens.ShouldHaveSingleItem();
        token.Type.ShouldBe(UserTokenType.PasswordReset);
        // El generador es el mismo para los tokens de los correos y los refresh tokens.
        token.TokenHash.ShouldBe(Factory.HashRefreshToken(Factory.PasswordResetTokensFor(Email)[^1]));
        token.IsValidAt(Clock.UtcNow).ShouldBeTrue();
    }

    [Fact]
    public async Task Limpieza_no_toca_los_tokens_de_un_registro_pendiente()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        await RegisterPatientAsync(PendingEmail, Password);
        Clock.UtcNow += PastRetention;

        await RunPurgeAsync();

        // Prueba que la limpieza corrió: RunOnceAsync registra los fallos sin propagarlos.
        (await FindPatientAsync(Email)).Tokens.ShouldBeEmpty();
        var pending = await FindPatientAsync(PendingEmail);
        pending.IsEmailConfirmed.ShouldBeFalse();
        pending.Tokens.ShouldHaveSingleItem().Type.ShouldBe(UserTokenType.EmailConfirmation);
    }

    private async Task LogoutAsync(string refreshToken)
    {
        using var response = await Client.PostAsJsonAsync(LogoutUrl, new LogoutRequest(refreshToken));
        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    private async Task RequestPasswordResetAsync()
    {
        using var response = await ForgotPasswordAsync(Email);
        await response.ShouldHaveStatusAsync(HttpStatusCode.Accepted);
    }

    private async Task<User> FindPatientAsync(string email) =>
        await FindUserAsync(email) ?? throw new InvalidOperationException($"No hay un usuario con el correo {email}.");

    private Task RunPurgeAsync() =>
        Factory.GetJob<PurgeExpiredCredentialsJob>().RunOnceAsync(CancellationToken.None);
}
