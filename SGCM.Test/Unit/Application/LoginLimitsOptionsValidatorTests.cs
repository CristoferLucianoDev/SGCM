using Shouldly;
using SGCM.Data.Application.Identity;

namespace SGCM.Test.Unit.Application;

public class LoginLimitsOptionsValidatorTests
{
    private readonly LoginLimitsOptionsValidator _validator = new();

    private static LoginLimitsOptions ValidOptions() => new()
    {
        MaxFailedAttemptsPerEmail = 5,
        EmailWindowMinutes = 15,
        MaxRequestsPerIp = 20,
        IpWindowSeconds = 60
    };

    [Fact]
    public void Valid_configuration_passes()
    {
        _validator.Validate(null, ValidOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(nameof(LoginLimitsOptions.MaxFailedAttemptsPerEmail))]
    [InlineData(nameof(LoginLimitsOptions.EmailWindowMinutes))]
    [InlineData(nameof(LoginLimitsOptions.MaxRequestsPerIp))]
    [InlineData(nameof(LoginLimitsOptions.IpWindowSeconds))]
    public void Zero_value_fails_and_names_its_key(string property)
    {
        var options = ValidOptions();
        typeof(LoginLimitsOptions).GetProperty(property)!.SetValue(options, 0);

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain($"{LoginLimitsOptions.SectionName}:{property}");
    }

    [Theory]
    [InlineData(nameof(LoginLimitsOptions.EmailWindowMinutes), LoginLimitsOptionsValidator.MaxEmailWindowMinutes)]
    [InlineData(nameof(LoginLimitsOptions.IpWindowSeconds), LoginLimitsOptionsValidator.MaxIpWindowSeconds)]
    public void Window_above_the_maximum_fails_and_names_its_key(string property, int max)
    {
        var options = ValidOptions();
        typeof(LoginLimitsOptions).GetProperty(property)!.SetValue(options, max + 1);

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain($"{LoginLimitsOptions.SectionName}:{property}");
    }

    [Theory]
    [InlineData(nameof(LoginLimitsOptions.EmailWindowMinutes), LoginLimitsOptionsValidator.MaxEmailWindowMinutes)]
    [InlineData(nameof(LoginLimitsOptions.IpWindowSeconds), LoginLimitsOptionsValidator.MaxIpWindowSeconds)]
    public void Window_at_the_maximum_passes(string property, int max)
    {
        var options = ValidOptions();
        typeof(LoginLimitsOptions).GetProperty(property)!.SetValue(options, max);

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }
}
