using Shouldly;
using SGCM.Data.Application.Identity;

namespace SGCM.Test.Unit.Application;

public class PasswordResetLimitsOptionsValidatorTests
{
    private readonly PasswordResetLimitsOptionsValidator _validator = new();

    private static PasswordResetLimitsOptions ValidOptions() => new()
    {
        MaxRequestsPerEmail = 3,
        EmailWindowMinutes = 15,
        MaxRequestsPerIp = 10,
        IpWindowSeconds = 60
    };

    [Fact]
    public void Valid_configuration_passes()
    {
        _validator.Validate(null, ValidOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(nameof(PasswordResetLimitsOptions.MaxRequestsPerEmail), 0)]
    [InlineData(nameof(PasswordResetLimitsOptions.EmailWindowMinutes), 0)]
    [InlineData(nameof(PasswordResetLimitsOptions.MaxRequestsPerIp), 0)]
    [InlineData(nameof(PasswordResetLimitsOptions.IpWindowSeconds), 0)]
    [InlineData(nameof(PasswordResetLimitsOptions.MaxRequestsPerEmail), -1)]
    [InlineData(nameof(PasswordResetLimitsOptions.EmailWindowMinutes), -1)]
    [InlineData(nameof(PasswordResetLimitsOptions.MaxRequestsPerIp), -1)]
    [InlineData(nameof(PasswordResetLimitsOptions.IpWindowSeconds), -1)]
    public void Value_not_positive_fails_and_names_its_key(string property, int value)
    {
        var options = ValidOptions();
        typeof(PasswordResetLimitsOptions).GetProperty(property)!.SetValue(options, value);

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain($"{PasswordResetLimitsOptions.SectionName}:{property}");
    }

    [Theory]
    [InlineData(nameof(PasswordResetLimitsOptions.EmailWindowMinutes), LoginLimitsOptionsValidator.MaxEmailWindowMinutes)]
    [InlineData(nameof(PasswordResetLimitsOptions.IpWindowSeconds), LoginLimitsOptionsValidator.MaxIpWindowSeconds)]
    public void Window_above_the_maximum_fails_and_names_its_key(string property, int max)
    {
        var options = ValidOptions();
        typeof(PasswordResetLimitsOptions).GetProperty(property)!.SetValue(options, max + 1);

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain($"{PasswordResetLimitsOptions.SectionName}:{property}");
    }

    [Theory]
    [InlineData(nameof(PasswordResetLimitsOptions.EmailWindowMinutes), LoginLimitsOptionsValidator.MaxEmailWindowMinutes)]
    [InlineData(nameof(PasswordResetLimitsOptions.IpWindowSeconds), LoginLimitsOptionsValidator.MaxIpWindowSeconds)]
    public void Window_at_the_maximum_passes(string property, int max)
    {
        var options = ValidOptions();
        typeof(PasswordResetLimitsOptions).GetProperty(property)!.SetValue(options, max);

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }
}
