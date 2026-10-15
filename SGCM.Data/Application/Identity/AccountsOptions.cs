using Microsoft.Extensions.Options;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Application.Identity;

public sealed class AccountsOptions
{
    public const string SectionName = "Accounts";

    public int EmailConfirmationTokenMinutes { get; set; }
    public int ResendConfirmationCooldownSeconds { get; set; }
    public int PasswordResetTokenMinutes { get; set; }
    public string EmailConfirmationUrl { get; set; } = string.Empty;

    /// <summary>
    /// URL absoluta https de la página que restablece la contraseña; el enlace del correo le
    /// agrega el token.
    /// </summary>
    public string PasswordResetUrl { get; set; } = string.Empty;
}

public sealed class AccountsOptionsValidator : IValidateOptions<AccountsOptions>
{
    public ValidateOptionsResult Validate(string? name, AccountsOptions options)
    {
        var errors = new List<string>();

        OptionsRules.Positive(errors, "Accounts:EmailConfirmationTokenMinutes", options.EmailConfirmationTokenMinutes);
        OptionsRules.Positive(errors, "Accounts:ResendConfirmationCooldownSeconds", options.ResendConfirmationCooldownSeconds);
        OptionsRules.Positive(errors, "Accounts:PasswordResetTokenMinutes", options.PasswordResetTokenMinutes);
        OptionsRules.Required(errors, "Accounts:EmailConfirmationUrl", options.EmailConfirmationUrl);
        OptionsRules.AbsoluteHttpsUrl(errors, "Accounts:EmailConfirmationUrl", options.EmailConfirmationUrl);
        OptionsRules.Required(errors, "Accounts:PasswordResetUrl", options.PasswordResetUrl);
        OptionsRules.AbsoluteHttpsUrl(errors, "Accounts:PasswordResetUrl", options.PasswordResetUrl);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}