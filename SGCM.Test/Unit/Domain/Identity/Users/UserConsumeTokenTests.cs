using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas del consumo de tokens en <see cref="User.ConsumeToken"/>.
/// </summary>
public class UserConsumeTokenTests
{
    private static readonly DateTimeOffset Now = UserMother.Now;

    [Fact]
    public void ConsumeToken_con_token_vigente_lo_marca_como_usado()
    {
        var user = UserMother.Pending();
        var now = Now.AddMinutes(1);

        var result = user.ConsumeToken(UserTokenType.EmailConfirmation, UserMother.DefaultTokenHash, now);

        result.IsSuccess.ShouldBeTrue();
        var token = user.Tokens.Single();
        token.UsedAt.ShouldBe(now);
        token.IsValidAt(now).ShouldBeFalse();
    }

    [Fact]
    public void ConsumeToken_con_hash_inexistente_devuelve_TokenInvalid()
    {
        var user = UserMother.Pending();

        var result = user.ConsumeToken(UserTokenType.EmailConfirmation, "hash-inexistente", Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConsumeToken_con_hash_nulo_o_vacio_devuelve_TokenInvalid(string? tokenHash)
    {
        var user = UserMother.Pending();

        var result = user.ConsumeToken(UserTokenType.EmailConfirmation, tokenHash!, Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        user.Tokens.Single().UsedAt.ShouldBeNull();
    }

    [Fact]
    public void ConsumeToken_con_hash_de_otro_tipo_devuelve_TokenInvalid()
    {
        var user = UserMother.Pending();

        var result = user.ConsumeToken(UserTokenType.PasswordReset, UserMother.DefaultTokenHash, Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
    }

    [Fact]
    public void ConsumeToken_dos_veces_devuelve_TokenInvalid_la_segunda()
    {
        var user = UserMother.Pending();
        var now = Now.AddMinutes(1);
        user.ConsumeToken(UserTokenType.EmailConfirmation, UserMother.DefaultTokenHash, now);

        var result = user.ConsumeToken(UserTokenType.EmailConfirmation, UserMother.DefaultTokenHash, now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
    }

    [Fact]
    public void ConsumeToken_de_un_token_reemplazado_devuelve_TokenInvalid()
    {
        var user = UserMother.Pending();
        var now = Now.AddMinutes(1);
        user.IssueToken(UserTokenType.EmailConfirmation, "raw-2", "hash-2", now, UserMother.ConfirmationLifetime);

        var result = user.ConsumeToken(UserTokenType.EmailConfirmation, UserMother.DefaultTokenHash, now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.TokenInvalid);
    }

    [Fact]
    public void ConsumeToken_de_un_token_vencido_devuelve_TokenExpired_y_no_lo_marca()
    {
        var user = UserMother.Pending();
        var now = Now + UserMother.ConfirmationLifetime;

        var result = user.ConsumeToken(UserTokenType.EmailConfirmation, UserMother.DefaultTokenHash, now);

        result.Error.ShouldBe(UserErrors.TokenExpired);
        user.Tokens.Single().UsedAt.ShouldBeNull();
    }
}
