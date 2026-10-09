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
}