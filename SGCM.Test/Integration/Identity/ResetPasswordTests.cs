using System.Net;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;

namespace SGCM.Test.Integration.Identity;

[Trait("Category", "Integration")]
public sealed class ResetPasswordTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Email = "paciente@correo.com";
    private const string DoctorEmail = "medico@correo.com";
    private const string Password = "Secreta123";
    private const string NewPassword = "Renovada123";
    private const string AnotherNewPassword = "Distinta123";
    private const string WrongPassword = "Incorrecta123";
    private const string ShortPassword = "Corta12";
    private const string UnknownToken = "token-que-no-existe";

    // Vigencia del enlace de recuperación y límite de intentos de inicio de sesión según el PBI.
    private static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromMinutes(10);
    private const int MaxFailedLoginAttempts = 5;

    // Lleva el reloj claramente después del vencimiento, no justo en el límite.
    private static readonly TimeSpan MarginAfterExpiry = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Un_enlace_vigente_cambia_la_contrasena()
    {
        var token = await RequestResetLinkAsync(Email);

        var response = await ResetPasswordAsync(token, NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await (await LoginAsync(Email, NewPassword)).ShouldHaveStatusAsync(HttpStatusCode.OK);
        await (await LoginAsync(Email, Password)).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Restablecer_cierra_todas_las_sesiones()
    {
        var token = await RequestResetLinkAsync(Email);
        await LoginSuccessfullyAsync(Email, Password);
        await LoginSuccessfullyAsync(Email, Password);

        await (await ResetPasswordAsync(token, NewPassword)).ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        // El repositorio traduce revocation_reason = 'password_reset' a SessionRevocationReason.PasswordReset.
        var sessions = (await FindUserAsync(Email))!.Sessions;
        sessions.Count.ShouldBe(2);
        sessions.ShouldAllBe(session =>
            !session.IsActiveAt(Clock.UtcNow) && session.RevocationReason == SessionRevocationReason.PasswordReset);
    }

    [Fact]
    public async Task Un_enlace_usado_se_rechaza()
    {
        var token = await RequestResetLinkAsync(Email);
        await (await ResetPasswordAsync(token, NewPassword)).ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        var response = await ResetPasswordAsync(token, AnotherNewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.TokenInvalid.Code);
        await (await LoginAsync(Email, NewPassword)).ShouldHaveStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Un_enlace_vencido_se_rechaza()
    {
        var token = await RequestResetLinkAsync(Email);
        Clock.UtcNow += ResetLinkLifetime;

        var response = await ResetPasswordAsync(token, NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.TokenExpired.Code);
        await (await LoginAsync(Email, Password)).ShouldHaveStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Un_enlace_vencido_y_reemplazado_responde_TokenInvalid()
    {
        var expiredToken = await RequestResetLinkAsync(Email);
        Clock.UtcNow += ResetLinkLifetime + MarginAfterExpiry;

        // El enlace vencido sigue pendiente; la solicitud nueva debe invalidarlo para no violar
        // el índice único de tokens pendientes por usuario y tipo.
        var latestToken = await RequestResetTokenAsync(Email);
        latestToken.ShouldNotBe(expiredToken);

        var replaced = await ResetPasswordAsync(expiredToken, NewPassword);

        await replaced.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await replaced.ReadErrorCodeAsync()).ShouldBe(UserErrors.TokenInvalid.Code);

        var response = await ResetPasswordAsync(latestToken, NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Un_token_inexistente_se_rechaza()
    {
        var response = await ResetPasswordAsync(UnknownToken, NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.TokenInvalid.Code);
    }

    [Fact]
    public async Task Una_contrasena_corta_se_rechaza_sin_consumir_el_enlace()
    {
        var token = await RequestResetLinkAsync(Email);

        var rejected = await ResetPasswordAsync(token, ShortPassword);

        await rejected.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await rejected.ReadErrorCodeAsync()).ShouldBe(UserErrors.PasswordTooShort.Code);

        var response = await ResetPasswordAsync(token, NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Una_cuenta_desactivada_despues_de_la_solicitud_responde_403()
    {
        var token = await RequestResetLinkAsync(Email);
        var passwordHashBefore = (await FindUserAsync(Email))!.PasswordHash;
        await Factory.UpdateUserAsync(Email, user => user.Deactivate().IsSuccess.ShouldBeTrue());

        var response = await ResetPasswordAsync(token, NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.AccountInactive.Code);

        // El login de una cuenta inactiva responde 401 con cualquier contraseña, así que el
        // cambio se descarta mirando la base.
        var user = (await FindUserAsync(Email))!;
        user.PasswordHash.ShouldBe(passwordHashBefore);
        user.Tokens.Single(userToken => userToken.Type == UserTokenType.PasswordReset).UsedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Restablecer_desbloquea_el_inicio_de_sesion()
    {
        var token = await RequestResetLinkAsync(Email);
        for (var attempt = 0; attempt < MaxFailedLoginAttempts; attempt++)
        {
            await (await LoginAsync(Email, WrongPassword)).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        }

        await (await LoginAsync(Email, Password)).ShouldHaveStatusAsync(HttpStatusCode.TooManyRequests);
        await (await ResetPasswordAsync(token, NewPassword)).ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        var response = await LoginAsync(Email, NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Un_medico_con_contrasena_temporal_pasa_al_paso_de_perfil()
    {
        await SaveUserAsync(UserRole.Doctor, DoctorEmail, Password, mustChangePassword: true);
        (await LoginSuccessfullyAsync(DoctorEmail, Password)).PendingStep
            .ShouldBe(PendingStepClaimValues.ChangeTemporaryPassword);
        var token = await RequestResetTokenAsync(DoctorEmail);

        await (await ResetPasswordAsync(token, NewPassword)).ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        // Sin perfil de médico, el paso siguiente solo aparece si la contraseña temporal quedó cumplida.
        (await LoginSuccessfullyAsync(DoctorEmail, NewPassword)).PendingStep
            .ShouldBe(PendingStepClaimValues.DoctorProfile);
    }

    // Crea un paciente confirmado, pide el enlace de recuperación y devuelve su token en claro.
    private async Task<string> RequestResetLinkAsync(string email)
    {
        await CreateConfirmedPatientAsync(email, Password);

        return await RequestResetTokenAsync(email);
    }

    // Pide el enlace de recuperación de una cuenta ya existente y devuelve su token en claro.
    private async Task<string> RequestResetTokenAsync(string email)
    {
        await (await ForgotPasswordAsync(email)).ShouldHaveStatusAsync(HttpStatusCode.Accepted);

        // El 202 no prueba que se haya enviado el correo; sin esta aserción, [^1] fallaría
        // con un índice fuera de rango que no explica la causa.
        var tokens = Factory.PasswordResetTokensFor(email);
        tokens.ShouldNotBeEmpty();

        return tokens[^1];
    }
}
