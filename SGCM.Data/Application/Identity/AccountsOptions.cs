using Microsoft.Extensions.Options;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Application.Identity;

public sealed class AccountsOptions
{
    public const string SectionName = "Accounts";

    /// <summary>
    /// Máximo de días admitido para <see cref="CredentialRetentionDays"/>. Con un valor enorme,
    /// el cálculo del corte desbordaría en cada ejecución del proceso en vez de fallar al arrancar.
    /// </summary>
    public const int MaxCredentialRetentionDays = 365;

    public int EmailConfirmationTokenMinutes { get; set; }
    public int ResendConfirmationCooldownSeconds { get; set; }
    public int PasswordResetTokenMinutes { get; set; }
    public string EmailConfirmationUrl { get; set; } = string.Empty;

    /// <summary>
    /// URL absoluta https de la página que restablece la contraseña; el enlace del correo le
    /// agrega el token.
    /// </summary>
    public string PasswordResetUrl { get; set; } = string.Empty;

    /// <summary>
    /// Días que se conservan los tokens y las sesiones después de terminar, antes de que los
    /// borre el proceso periódico. Debe estar entre 1 y <see cref="MaxCredentialRetentionDays"/>.
    /// </summary>
    public int CredentialRetentionDays { get; set; }
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
        OptionsRules.Positive(errors, "Accounts:CredentialRetentionDays", options.CredentialRetentionDays);
        OptionsRules.AtMost(errors, "Accounts:CredentialRetentionDays", options.CredentialRetentionDays, AccountsOptions.MaxCredentialRetentionDays);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}