using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas del vencimiento del registro pendiente en <see cref="User.IsPendingRegistrationExpiredAt"/>
/// y de la confirmación de correo en <see cref="User.ConfirmEmail"/>.
/// </summary>
public class UserConfirmEmailTests
{
    private static readonly DateTimeOffset Now = UserMother.Now;

    [Fact]
    public void IsPendingRegistrationExpiredAt_con_token_vigente_devuelve_false()
    {
        var user = UserMother.Pending();

        user.IsPendingRegistrationExpiredAt(Now.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void IsPendingRegistrationExpiredAt_con_el_ultimo_token_vencido_devuelve_true()
    {
        var user = UserMother.Pending();

        user.IsPendingRegistrationExpiredAt(Now + UserMother.ConfirmationLifetime).ShouldBeTrue();
    }

    [Fact]
    public void IsPendingRegistrationExpiredAt_tras_reemplazar_el_token_usa_el_ultimo()
    {
        var user = UserMother.Pending();
        user.IssueToken(UserTokenType.EmailConfirmation, "raw-2", "hash-2", Now.AddMinutes(10), UserMother.ConfirmationLifetime);

        user.IsPendingRegistrationExpiredAt(Now + UserMother.ConfirmationLifetime).ShouldBeFalse();
    }

    [Fact]
    public void IsPendingRegistrationExpiredAt_de_una_cuenta_confirmada_devuelve_false()
    {
        var user = UserMother.Confirmed();

        user.IsPendingRegistrationExpiredAt(Now.AddDays(1)).ShouldBeFalse();
    }

    [Fact]
    public void IsPendingRegistrationExpiredAt_ignora_tokens_de_otro_tipo()
    {
        var user = UserMother.Pending();
        user.IssueToken(UserTokenType.PasswordReset, "raw-reset", "hash-reset", Now.AddMinutes(10), TimeSpan.FromMinutes(30));

        user.IsPendingRegistrationExpiredAt(Now + UserMother.ConfirmationLifetime).ShouldBeTrue();
    }

    [Fact]
    public void ConfirmEmail_con_token_vigente_confirma_la_cuenta_y_marca_el_token()
    {
        var user = UserMother.Pending();
        var now = Now.AddMinutes(1);

        var result = user.ConfirmEmail(UserMother.DefaultTokenHash, now);

        result.IsSuccess.ShouldBeTrue();
        user.IsEmailConfirmed.ShouldBeTrue();
        user.Tokens.Single().UsedAt.ShouldBe(now);
    }

    [Fact]
    public void ConfirmEmail_con_registro_vencido_devuelve_RegistrationExpired()
    {
        var user = UserMother.Pending();

        var result = user.ConfirmEmail(UserMother.DefaultTokenHash, Now + UserMother.ConfirmationLifetime);

        result.Error.ShouldBe(UserErrors.RegistrationExpired);
        user.IsEmailConfirmed.ShouldBeFalse();
        user.Tokens.Single().UsedAt.ShouldBeNull();
    }

    [Fact]
    public void ConfirmEmail_con_enlace_reemplazado_devuelve_TokenInvalid()
    {
        var user = UserMother.Pending();
        var now = Now.AddMinutes(1);
        user.IssueToken(UserTokenType.EmailConfirmation, "raw-2", "hash-2", now, UserMother.ConfirmationLifetime);

        var result = user.ConfirmEmail(UserMother.DefaultTokenHash, now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        user.IsEmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void ConfirmEmail_dos_veces_devuelve_TokenInvalid_la_segunda()
    {
        var user = UserMother.Pending();
        var now = Now.AddMinutes(1);
        user.ConfirmEmail(UserMother.DefaultTokenHash, now);

        var result = user.ConfirmEmail(UserMother.DefaultTokenHash, now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        user.IsEmailConfirmed.ShouldBeTrue();
    }

    [Fact]
    public void ConfirmEmail_de_una_cuenta_desactivada_devuelve_AccountInactive()
    {
        var user = UserMother.Pending();
        user.Deactivate();

        var result = user.ConfirmEmail(UserMother.DefaultTokenHash, Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.AccountInactive);
        user.IsEmailConfirmed.ShouldBeFalse();
    }
}
