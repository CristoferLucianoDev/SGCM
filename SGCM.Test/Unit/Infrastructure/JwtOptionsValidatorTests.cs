using Shouldly;
using SGCM.Data.Infrastructure.Security;

namespace SGCM.Test.Unit.Infrastructure;

public class JwtOptionsValidatorTests
{
    private readonly JwtOptionsValidator _validator = new();

    private static JwtOptions ValidOptions() => new()
    {
        Issuer = "SGCM",
        Audience = "SGCM",
        SigningKey = new string('k', JwtOptions.MinimumSigningKeyBytes),
        AccessTokenMinutes = 15,
        RefreshTokenDays = 7,
        MaxActiveSessionsPerUser = 5,
        ClockSkewSeconds = 30
    };

    [Fact]
    public void Valid_configuration_passes()
    {
        _validator.Validate(null, ValidOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Missing_signing_key_fails_and_names_the_environment_variable()
    {
        var options = ValidOptions();
        options.SigningKey = null;

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Jwt__SigningKey");
    }

    [Fact]
    public void Signing_key_shorter_than_minimum_fails()
    {
        var options = ValidOptions();
        options.SigningKey = new string('k', JwtOptions.MinimumSigningKeyBytes - 1);

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("al menos 32 bytes");
    }
}