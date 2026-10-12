using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Identity.Users.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas del reenvío del correo de confirmación en <see cref="User.ResendEmailConfirmation"/>.
/// </summary>
public class UserResendEmailConfirmationTests
{
    private static readonly DateTimeOffset Now = UserMother.Now;

    // Espera mínima entre envíos del correo de confirmación que fija el PBI.
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    [Fact]
    public void ResendEmailConfirmation_tras_la_espera_emite_un_token_nuevo_e_invalida_el_anterior()
    {
        var user = UserMother.Pending();
        var at = Now + Cooldown;

        var result = Resend(user, at);

        result.IsSuccess.ShouldBeTrue();
        user.Tokens.Count.ShouldBe(2);
        var validToken = user.Tokens.Where(token => token.IsValidAt(at)).ShouldHaveSingleItem();
        validToken.TokenHash.ShouldBe("hash-reenvio");
        validToken.ExpiresAt.ShouldBe(Now + Cooldown + UserMother.ConfirmationLifetime);
        user.DomainEvents.OfType<UserTokenIssuedEvent>().Last().RawToken.ShouldBe("raw-reenvio");
    }

    [Fact]
    public void ResendEmailConfirmation_antes_de_la_espera_se_rechaza()
    {
        var user = UserMother.Pending();
        user.ClearDomainEvents();
        var at = Now + Cooldown - TimeSpan.FromSeconds(1);

        var result = Resend(user, at);

        result.Error.ShouldBe(UserErrors.ResendTooSoon);
        user.Tokens.ShouldHaveSingleItem().IsValidAt(at).ShouldBeTrue();
        user.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ResendEmailConfirmation_mide_la_espera_desde_el_ultimo_token()
    {
        var user = UserMother.Pending();
        var lastIssuedAt = Now.AddMinutes(10);
        user.IssueToken(UserTokenType.EmailConfirmation, "raw-2", "hash-2", lastIssuedAt, UserMother.ConfirmationLifetime);
        user.ClearDomainEvents();

        var result = Resend(user, lastIssuedAt + TimeSpan.FromSeconds(10));

        result.Error.ShouldBe(UserErrors.ResendTooSoon);
    }

    [Fact]
    public void ResendEmailConfirmation_con_el_ultimo_token_vencido_indica_registrarse_de_nuevo()
    {
        var user = UserMother.Pending();
        user.ClearDomainEvents();

        var result = Resend(user, Now + UserMother.ConfirmationLifetime);

        result.Error.ShouldBe(UserErrors.RegistrationExpired);
        user.Tokens.ShouldHaveSingleItem();
        user.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ResendEmailConfirmation_de_una_cuenta_confirmada_se_rechaza()
    {
        var user = UserMother.Pending();
        user.ConfirmEmail(UserMother.DefaultTokenHash, Now);
        user.ClearDomainEvents();

        var result = Resend(user, Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.EmailAlreadyConfirmed);
        user.Tokens.ShouldHaveSingleItem();
    }

    [Fact]
    public void ResendEmailConfirmation_de_una_cuenta_inactiva_se_rechaza()
    {
        var user = UserMother.Pending();
        user.Deactivate();
        user.ClearDomainEvents();

        var result = Resend(user, Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.AccountInactive);
        user.Tokens.ShouldHaveSingleItem();
    }

    [Fact]
    public void ResendEmailConfirmation_ignora_tokens_de_otro_tipo_para_la_espera()
    {
        var user = UserMother.Pending();
        var resetIssuedAt = Now.AddMinutes(1);
        user.IssueToken(UserTokenType.PasswordReset, "raw-reset", "hash-reset", resetIssuedAt, TimeSpan.FromMinutes(30));

        var result = Resend(user, resetIssuedAt + TimeSpan.FromSeconds(10));

        result.IsSuccess.ShouldBeTrue();
    }

    private static Result Resend(User user, DateTimeOffset at) =>
        user.ResendEmailConfirmation("raw-reenvio", "hash-reenvio", at, UserMother.ConfirmationLifetime, Cooldown);
}
