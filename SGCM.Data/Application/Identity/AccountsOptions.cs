using Microsoft.Extensions.Options;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Application.Identity;

public sealed class AccountsOptions
{
    public const string SectionName = "Accounts";

    public int EmailConfirmationTokenMinutes { get; set; }
    public int ResendConfirmationCooldownSeconds { get; set; }
    public int PasswordResetTokenMinutes { get; set; }
}

public sealed class AccountsOptionsValidator : IValidateOptions<AccountsOptions>
{
    public ValidateOptionsResult Validate(string? name, AccountsOptions options)
    {
        var errors = new List<string>();

        OptionsRules.Positive(errors, "Accounts:EmailConfirmationTokenMinutes", options.EmailConfirmationTokenMinutes);
        OptionsRules.Positive(errors, "Accounts:ResendConfirmationCooldownSeconds", options.ResendConfirmationCooldownSeconds);
        OptionsRules.Positive(errors, "Accounts:PasswordResetTokenMinutes", options.PasswordResetTokenMinutes);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}