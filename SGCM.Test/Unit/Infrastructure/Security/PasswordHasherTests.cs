using Shouldly;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using IdentityPasswordHasher = Microsoft.AspNetCore.Identity.PasswordHasher<object>;
using PasswordVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace SGCM.Test.Unit.Infrastructure.Security;

public sealed class PasswordHasherTests
{
    private static readonly Password ValidPassword = Password.Create("Secreta123").Value;

    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_DoesNotContainPlainPassword()
    {
        var hash = _hasher.Hash(ValidPassword);

        hash.ShouldNotContain(ValidPassword.Value);
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        var first = _hasher.Hash(ValidPassword);
        var second = _hasher.Hash(ValidPassword);

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Hash_CanBeVerifiedByIdentityHasher()
    {
        var hash = _hasher.Hash(ValidPassword);

        var result = new IdentityPasswordHasher().VerifyHashedPassword(new object(), hash, ValidPassword.Value);

        result.ShouldBe(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_con_la_contrasena_correcta_devuelve_true()
    {
        var hash = _hasher.Hash(ValidPassword);

        _hasher.Verify(hash, ValidPassword.Value).ShouldBeTrue();
    }

    [Fact]
    public void Verify_con_otra_contrasena_devuelve_false()
    {
        var hash = _hasher.Hash(ValidPassword);

        _hasher.Verify(hash, "OtraClave456").ShouldBeFalse();
    }

    // Un hash vacío se decodifica como cero bytes; uno que no es Base64 hace lanzar a Identity.
    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-hash")]
    public void Verify_con_un_hash_mal_formado_devuelve_false(string malformedHash)
    {
        _hasher.Verify(malformedHash, ValidPassword.Value).ShouldBeFalse();
    }

    [Fact]
    public void Verify_sin_hash_devuelve_false()
    {
        _hasher.Verify(null, ValidPassword.Value).ShouldBeFalse();
    }
}