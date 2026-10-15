using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas del restablecimiento de contraseña en <see cref="User.ResetPassword"/>.
/// </summary>
public class UserResetPasswordTests
{
    private const string ResetRawToken = "raw-reset";
    private const string ResetTokenHash = "hash-reset";
    private const string NewPasswordHash = "hash-nueva";
    private const string DeviceName = "Chrome en Windows";
    private const string IpAddress = "203.0.113.10";
    private const int MaxSessions = 5;

    private static readonly DateTimeOffset Now = UserMother.Now;

    // Vigencia del enlace de recuperación que fija el PBI y de la sesión que fija la arquitectura.
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    [Fact]
    public void ResetPassword_con_token_vigente_cambia_el_hash_usa_el_token_y_revoca_las_sesiones()
    {
        var user = UserWithResetToken();
        StartSession(user, "hash-sesion-1");
        StartSession(user, "hash-sesion-2");
        var at = Now.AddMinutes(1);

        var result = user.ResetPassword(ResetTokenHash, NewPasswordHash, at);

        result.IsSuccess.ShouldBeTrue();
        user.PasswordHash.ShouldBe(NewPasswordHash);
        ResetToken(user).UsedAt.ShouldBe(at);
        user.Sessions.ShouldAllBe(session =>
            session.RevokedAt == at && session.RevocationReason == SessionRevocationReason.PasswordReset);
    }

    [Fact]
    public void ResetPassword_no_toca_las_sesiones_ya_revocadas()
    {
        var user = UserMother.Confirmed();
        StartSession(user, "hash-vieja");
        user.StartSession("hash-nueva-sesion", DeviceName, IpAddress, Now.AddMinutes(1), SessionLifetime, 1);
        var revokedEarlier = user.Sessions.Single(session => session.RefreshTokenHash == "hash-vieja");
        var revokedAt = revokedEarlier.RevokedAt;
        user.IssueToken(UserTokenType.PasswordReset, ResetRawToken, ResetTokenHash, Now.AddMinutes(2), ResetLifetime);

        var result = user.ResetPassword(ResetTokenHash, NewPasswordHash, Now.AddMinutes(3));

        result.IsSuccess.ShouldBeTrue();
        revokedEarlier.RevokedAt.ShouldBe(revokedAt);
        revokedEarlier.RevocationReason.ShouldBe(SessionRevocationReason.SessionLimit);
    }

    [Fact]
    public void ResetPassword_cumple_el_paso_de_contrasena_temporal()
    {
        var user = UserMother.WithRole(UserRole.Doctor, mustChangePassword: true);
        user.IssueToken(UserTokenType.PasswordReset, ResetRawToken, ResetTokenHash, Now, ResetLifetime);

        user.ResetPassword(ResetTokenHash, NewPasswordHash, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        user.MustChangePassword.ShouldBeFalse();
    }

    [Fact]
    public void ResetPassword_con_token_vencido_devuelve_TokenExpired_sin_cambios()
    {
        var user = UserWithResetToken();

        var result = user.ResetPassword(ResetTokenHash, NewPasswordHash, Now + ResetLifetime);

        result.Error.ShouldBe(UserErrors.TokenExpired);
        ShouldBeUnchanged(user);
    }

    [Fact]
    public void ResetPassword_con_token_usado_devuelve_TokenInvalid()
    {
        var user = UserWithResetToken();
        user.ResetPassword(ResetTokenHash, NewPasswordHash, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        var result = user.ResetPassword(ResetTokenHash, "hash-otra", Now.AddMinutes(2));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        user.PasswordHash.ShouldBe(NewPasswordHash);
    }

    [Fact]
    public void ResetPassword_con_token_reemplazado_devuelve_TokenInvalid_sin_cambios()
    {
        var user = UserWithResetToken();
        user.IssueToken(UserTokenType.PasswordReset, "raw-reset-2", "hash-reset-2", Now.AddMinutes(1), ResetLifetime);

        var result = user.ResetPassword(ResetTokenHash, NewPasswordHash, Now.AddMinutes(2));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        user.PasswordHash.ShouldBe(UserMother.DefaultPasswordHash);
    }

    [Fact]
    public void ResetPassword_con_token_inexistente_devuelve_TokenInvalid_sin_cambios()
    {
        var user = UserWithResetToken();

        var result = user.ResetPassword("hash-inexistente", NewPasswordHash, Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        ShouldBeUnchanged(user);
    }

    [Fact]
    public void ResetPassword_no_acepta_un_token_de_confirmacion_de_correo()
    {
        var user = UserMother.Confirmed();

        var result = user.ResetPassword(UserMother.DefaultTokenHash, NewPasswordHash, Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        user.PasswordHash.ShouldBe(UserMother.DefaultPasswordHash);
        user.Tokens.ShouldHaveSingleItem().UsedAt.ShouldBeNull();
    }

    [Fact]
    public void ResetPassword_de_una_cuenta_inactiva_devuelve_AccountInactive_sin_cambios()
    {
        var user = UserWithResetToken();
        user.Deactivate();

        var result = user.ResetPassword(ResetTokenHash, NewPasswordHash, Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.AccountInactive);
        ShouldBeUnchanged(user);
    }

    [Fact]
    public void ResetPassword_de_una_cuenta_sin_confirmar_devuelve_TokenInvalid_sin_cambios()
    {
        var user = UserMother.Pending();
        user.IssueToken(UserTokenType.PasswordReset, ResetRawToken, ResetTokenHash, Now, ResetLifetime);

        var result = user.ResetPassword(ResetTokenHash, NewPasswordHash, Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        ShouldBeUnchanged(user);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResetPassword_sin_hash_de_contrasena_lanza_DomainException(string passwordHash)
    {
        var user = UserWithResetToken();

        Should.Throw<DomainException>(() => user.ResetPassword(ResetTokenHash, passwordHash, Now.AddMinutes(1)));
    }

    [Fact]
    public void CanResetPassword_con_token_vigente_devuelve_exito_sin_modificar_al_usuario()
    {
        var user = UserWithResetToken();

        user.CanResetPassword(ResetTokenHash, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        ShouldBeUnchanged(user);
    }

    [Fact]
    public void CanResetPassword_con_token_vencido_devuelve_TokenExpired_sin_modificar_al_usuario()
    {
        var user = UserWithResetToken();

        user.CanResetPassword(ResetTokenHash, Now + ResetLifetime).Error.ShouldBe(UserErrors.TokenExpired);

        ShouldBeUnchanged(user);
    }

    [Fact]
    public void CanResetPassword_con_token_usado_devuelve_TokenInvalid_sin_modificar_al_usuario()
    {
        var user = UserWithResetToken();
        user.ResetPassword(ResetTokenHash, NewPasswordHash, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        var usedAt = ResetToken(user).UsedAt;

        user.CanResetPassword(ResetTokenHash, Now.AddMinutes(2)).Error.ShouldBe(UserErrors.TokenInvalid);

        user.PasswordHash.ShouldBe(NewPasswordHash);
        ResetToken(user).UsedAt.ShouldBe(usedAt);
    }

    [Fact]
    public void CanResetPassword_con_un_token_de_otro_tipo_devuelve_TokenInvalid_sin_modificar_al_usuario()
    {
        var user = UserMother.Confirmed();

        user.CanResetPassword(UserMother.DefaultTokenHash, Now.AddMinutes(1)).Error.ShouldBe(UserErrors.TokenInvalid);

        user.Tokens.ShouldHaveSingleItem().UsedAt.ShouldBeNull();
    }

    [Fact]
    public void CanResetPassword_de_una_cuenta_inactiva_devuelve_AccountInactive_sin_modificar_al_usuario()
    {
        var user = UserWithResetToken();
        user.Deactivate();

        user.CanResetPassword(ResetTokenHash, Now.AddMinutes(1)).Error.ShouldBe(UserErrors.AccountInactive);

        ShouldBeUnchanged(user);
    }

    private static User UserWithResetToken()
    {
        var user = UserMother.Confirmed();
        user.IssueToken(UserTokenType.PasswordReset, ResetRawToken, ResetTokenHash, Now, ResetLifetime);
        return user;
    }

    private static UserToken ResetToken(User user) =>
        user.Tokens.Single(token => token.TokenHash == ResetTokenHash);

    private static void StartSession(User user, string refreshTokenHash) =>
        user.StartSession(refreshTokenHash, DeviceName, IpAddress, Now, SessionLifetime, MaxSessions).IsSuccess.ShouldBeTrue();

    private static void ShouldBeUnchanged(User user)
    {
        user.PasswordHash.ShouldBe(UserMother.DefaultPasswordHash);
        ResetToken(user).UsedAt.ShouldBeNull();
    }
}
