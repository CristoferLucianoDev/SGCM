using Microsoft.Extensions.Options;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Application.Identity;

public sealed class LoginLimitsOptions
{
    public const string SectionName = "LoginLimits";

    public int MaxFailedAttemptsPerEmail { get; set; }
    public int EmailWindowMinutes { get; set; }
    public int MaxRequestsPerIp { get; set; }
    public int IpWindowSeconds { get; set; }
}

public sealed class LoginLimitsOptionsValidator : IValidateOptions<LoginLimitsOptions>
{
    // Tope de un día para las ventanas: un valor enorme desbordaría el fin de la ventana y
    // fallaría en cada inicio de sesión en lugar de al arrancar.
    public const int MaxEmailWindowMinutes = 24 * 60;
    public const int MaxIpWindowSeconds = 24 * 60 * 60;

    public ValidateOptionsResult Validate(string? name, LoginLimitsOptions options)
    {
        var errors = new List<string>();

        OptionsRules.Positive(errors, "LoginLimits:MaxFailedAttemptsPerEmail", options.MaxFailedAttemptsPerEmail);
        OptionsRules.Positive(errors, "LoginLimits:EmailWindowMinutes", options.EmailWindowMinutes);
        OptionsRules.Positive(errors, "LoginLimits:MaxRequestsPerIp", options.MaxRequestsPerIp);
        OptionsRules.Positive(errors, "LoginLimits:IpWindowSeconds", options.IpWindowSeconds);
        AtMost(errors, "LoginLimits:EmailWindowMinutes", options.EmailWindowMinutes, MaxEmailWindowMinutes);
        AtMost(errors, "LoginLimits:IpWindowSeconds", options.IpWindowSeconds, MaxIpWindowSeconds);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void AtMost(ICollection<string> errors, string key, int value, int max)
    {
        if (value > max)
        {
            errors.Add($"{OptionsRules.Describe(key)} no puede superar {max} (valor actual: {value}).");
        }
    }
}
