using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Identity.Users.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas de la emisión e invalidación de tokens en <see cref="User.IssueToken"/>
/// y de la vigencia de <see cref="UserToken"/>.
/// </summary>
public class UserIssueTokenTests
{
    private static readonly DateTimeOffset Now = UserMother.Now;

    [Fact]
    public void IssueToken_no_invalida_tokens_de_otro_tipo()
    {
        var user = UserMother.Pending();
        var later = Now.AddMinutes(1);

        user.IssueToken(UserTokenType.PasswordReset, "raw-reset", "hash-reset", later, TimeSpan.FromMinutes(30));

        user.Tokens.Count(token => token.IsValidAt(later)).ShouldBe(2);
    }

    [Fact]
    public void IssueToken_no_cambia_la_fecha_de_un_token_ya_invalidado()
    {
        var user = UserMother.Pending();
        var first = Now.AddMinutes(1);
        var second = Now.AddMinutes(2);

        user.IssueToken(UserTokenType.EmailConfirmation, "raw-2", "hash-2", first, UserMother.ConfirmationLifetime);
        user.IssueToken(UserTokenType.EmailConfirmation, "raw-3", "hash-3", second, UserMother.ConfirmationLifetime);

        user.Tokens.Single(token => token.TokenHash == UserMother.DefaultTokenHash).InvalidatedAt.ShouldBe(first);
        user.Tokens.Single(token => token.TokenHash == "hash-2").InvalidatedAt.ShouldBe(second);
        user.Tokens.Count(token => token.IsValidAt(second)).ShouldBe(1);
    }

    [Fact]
    public void IssueToken_dispara_un_evento_por_cada_emision()
    {
        var user = UserMother.Pending();

        user.IssueToken(UserTokenType.EmailConfirmation, "raw-2", "hash-2", Now.AddMinutes(1), UserMother.ConfirmationLifetime);

        user.DomainEvents.Count.ShouldBe(2);
        user.DomainEvents.OfType<UserTokenIssuedEvent>().Last().RawToken.ShouldBe("raw-2");
    }

    [Theory]
    [InlineData(null, "hash-2")]
    [InlineData("", "hash-2")]
    [InlineData("   ", "hash-2")]
    [InlineData("raw-2", null)]
    [InlineData("raw-2", "")]
    [InlineData("raw-2", "   ")]
    public void IssueToken_sin_token_o_sin_hash_lanza_DomainException_sin_modificar_el_usuario(string? rawToken, string? tokenHash)
    {
        var user = UserMother.Pending();

        Should.Throw<DomainException>(() => user.IssueToken(
            UserTokenType.EmailConfirmation, rawToken!, tokenHash!, Now.AddMinutes(1), UserMother.ConfirmationLifetime));

        user.Tokens.ShouldHaveSingleItem().IsValidAt(Now.AddMinutes(1)).ShouldBeTrue();
        user.DomainEvents.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IssueToken_con_vigencia_no_positiva_lanza_DomainException(int minutes)
    {
        var user = UserMother.Pending();

        Should.Throw<DomainException>(() => user.IssueToken(
            UserTokenType.PasswordReset, "raw-reset", "hash-reset", Now, TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void Un_token_deja_de_ser_valido_al_llegar_su_vencimiento()
    {
        var token = UserMother.Pending().Tokens.Single();

        token.IsValidAt(token.ExpiresAt.AddTicks(-1)).ShouldBeTrue();
        token.IsValidAt(token.ExpiresAt).ShouldBeFalse();
    }

    [Fact]
    public void El_evento_no_expone_el_token_en_ToString()
    {
        var domainEvent = new UserTokenIssuedEvent("paciente@correo.com", UserTokenType.EmailConfirmation, "token-secreto");

        var text = domainEvent.ToString();

        text.ShouldNotContain("token-secreto");
        text.ShouldContain("paciente@correo.com");
    }
}
