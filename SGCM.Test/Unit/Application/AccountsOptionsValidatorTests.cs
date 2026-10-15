using Shouldly;
using SGCM.Data.Application.Identity;

namespace SGCM.Test.Unit.Application;

public class AccountsOptionsValidatorTests
{
    private readonly AccountsOptionsValidator _validator = new();

    private static AccountsOptions ValidOptions() => new()
    {
        EmailConfirmationTokenMinutes = 15,
        ResendConfirmationCooldownSeconds = 30,
        PasswordResetTokenMinutes = 30,
        EmailConfirmationUrl = "https://localhost:7126/cuenta/confirmar",
        PasswordResetUrl = "https://localhost:7126/cuenta/restablecer"
    };

    [Fact]
    public void Valid_configuration_passes()
    {
        _validator.Validate(null, ValidOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_confirmation_url_fails_and_names_the_environment_variable(string url)
    {
        var options = ValidOptions();
        options.EmailConfirmationUrl = url;

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Accounts__EmailConfirmationUrl");
    }

    [Theory]
    [InlineData("http://localhost:7126/cuenta/confirmar")]
    [InlineData("/cuenta/confirmar")]
    [InlineData("no-es-una-url")]
    public void Confirmation_url_that_is_not_absolute_https_fails(string url)
    {
        var options = ValidOptions();
        options.EmailConfirmationUrl = url;

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("URL absoluta https");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_password_reset_url_fails_and_names_the_environment_variable(string url)
    {
        var options = ValidOptions();
        options.PasswordResetUrl = url;

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Accounts__PasswordResetUrl");
    }

    [Theory]
    [InlineData("http://localhost:7126/cuenta/restablecer")]
    [InlineData("/cuenta/restablecer")]
    [InlineData("no-es-una-url")]
    public void Password_reset_url_that_is_not_absolute_https_fails(string url)
    {
        var options = ValidOptions();
        options.PasswordResetUrl = url;

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Accounts__PasswordResetUrl");
        result.FailureMessage.ShouldContain("URL absoluta https");
    }
}