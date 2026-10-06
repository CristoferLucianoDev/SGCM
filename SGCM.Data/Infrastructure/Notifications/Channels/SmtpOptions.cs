using Microsoft.Extensions.Options;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Infrastructure.Notifications.Channels;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool UseStartTls { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
}

public sealed class SmtpOptionsValidator : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        var errors = new List<string>();

        OptionsRules.Required(errors, "Smtp:Host", options.Host);
        OptionsRules.Positive(errors, "Smtp:Port", options.Port);
        OptionsRules.Required(errors, "Smtp:SenderEmail", options.SenderEmail);
        OptionsRules.Required(errors, "Smtp:SenderName", options.SenderName);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}