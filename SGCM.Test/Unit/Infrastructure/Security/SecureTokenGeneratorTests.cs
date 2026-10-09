using Shouldly;
using SGCM.Data.Infrastructure.Security;

namespace SGCM.Test.Unit.Infrastructure.Security;

public sealed class SecureTokenGeneratorTests
{
    private const int Sha256HexLength = 64;

    private readonly SecureTokenGenerator _generator = new();

    [Fact]
    public void Generate_TwoCalls_ProduceDifferentValues()
    {
        var first = _generator.Generate();
        var second = _generator.Generate();

        first.Value.ShouldNotBe(second.Value);
    }

    [Fact]
    public void Generate_ValueIsUrlSafe()
    {
        var token = _generator.Generate();

        token.Value.ShouldNotContain("+");
        token.Value.ShouldNotContain("/");
        token.Value.ShouldNotContain("=");
    }

    [Fact]
    public void Generate_HashIsSha256HexAndDiffersFromValue()
    {
        var token = _generator.Generate();

        token.Hash.Length.ShouldBe(Sha256HexLength);
        token.Hash.ShouldNotBe(token.Value);
    }
}