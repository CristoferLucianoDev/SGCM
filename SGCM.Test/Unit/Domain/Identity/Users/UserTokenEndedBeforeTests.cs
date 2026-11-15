using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas de <see cref="UserToken.EndedBefore"/>, la regla que decide qué tokens borra el
/// proceso de retención.
/// </summary>
public class UserTokenEndedBeforeTests
{
    private const string ResetHash = "hash-reset";

    private static readonly DateTimeOffset Now = UserMother.Now;

    // Vigencia del enlace de recuperación que fija el PBI.
    private static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromMinutes(10);

    [Fact]
    public void EndedBefore_de_un_token_vigente_devuelve_false()
    {
        var token = IssueResetToken(UserMother.Confirmed());

        token.EndedBefore(Now.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void EndedBefore_de_un_token_vencido_antes_del_corte_devuelve_true()
    {
        var token = IssueResetToken(UserMother.Confirmed());

        token.EndedBefore(token.ExpiresAt.AddMinutes(1)).ShouldBeTrue();
    }

    [Fact]
    public void EndedBefore_con_el_vencimiento_igual_al_corte_devuelve_false()
    {
        var token = IssueResetToken(UserMother.Confirmed());

        token.EndedBefore(token.ExpiresAt).ShouldBeFalse();
    }

    [Fact]
    public void EndedBefore_de_un_token_usado_antes_del_corte_devuelve_true()
    {
        var user = UserMother.Confirmed();
        var token = IssueResetToken(user);
        var usedAt = Now.AddMinutes(1);
        user.ConsumeToken(UserTokenType.PasswordReset, ResetHash, usedAt).IsSuccess.ShouldBeTrue();

        token.EndedBefore(usedAt.AddMinutes(1)).ShouldBeTrue();
    }

    [Fact]
    public void EndedBefore_de_un_token_invalidado_antes_del_corte_devuelve_true()
    {
        var user = UserMother.Confirmed();
        var token = IssueResetToken(user);
        var replacedAt = Now.AddMinutes(1);
        user.IssueToken(UserTokenType.PasswordReset, "raw-reset-2", "hash-reset-2", replacedAt, PasswordResetLifetime);

        token.InvalidatedAt.ShouldBe(replacedAt);
        token.EndedBefore(replacedAt.AddMinutes(1)).ShouldBeTrue();
    }

    [Fact]
    public void EndedBefore_de_un_token_usado_despues_del_corte_y_no_vencido_devuelve_false()
    {
        var user = UserMother.Confirmed();
        var token = IssueResetToken(user);
        var cutoff = Now.AddMinutes(1);
        user.ConsumeToken(UserTokenType.PasswordReset, ResetHash, cutoff.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        token.EndedBefore(cutoff).ShouldBeFalse();
    }

    private static UserToken IssueResetToken(User user)
    {
        user.IssueToken(UserTokenType.PasswordReset, "raw-reset", ResetHash, Now, PasswordResetLifetime);
        return user.Tokens.Single(token => token.TokenHash == ResetHash);
    }
}
