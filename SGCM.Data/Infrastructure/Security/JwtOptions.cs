using Microsoft.Extensions.Options;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string? SigningKey { get; set; }
    public int AccessTokenMinutes { get; set; }
    public int RefreshTokenDays { get; set; }
    public int MaxActiveSessionsPerUser { get; set; }
    public int ClockSkewSeconds { get; set; }

    /// <summary>
    /// Segundos desde la última rotación durante los que presentar el refresh token anterior se
    /// trata como una renovación concurrente o un reintento, y no como robo. Con 0 solo se tolera
    /// el mismo instante de la rotación.
    /// </summary>
    public int RefreshReuseGraceSeconds { get; set; }
}

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var errors = new List<string>();

        OptionsRules.Required(errors, "Jwt:Issuer", options.Issuer);
        OptionsRules.Required(errors, "Jwt:Audience", options.Audience);
        OptionsRules.Required(errors, "Jwt:SigningKey", options.SigningKey);
        OptionsRules.MinimumBytes(errors, "Jwt:SigningKey", options.SigningKey, JwtOptions.MinimumSigningKeyBytes);
        OptionsRules.Positive(errors, "Jwt:AccessTokenMinutes", options.AccessTokenMinutes);
        OptionsRules.Positive(errors, "Jwt:RefreshTokenDays", options.RefreshTokenDays);
        OptionsRules.Positive(errors, "Jwt:MaxActiveSessionsPerUser", options.MaxActiveSessionsPerUser);
        OptionsRules.NonNegative(errors, "Jwt:ClockSkewSeconds", options.ClockSkewSeconds);
        OptionsRules.NonNegative(errors, "Jwt:RefreshReuseGraceSeconds", options.RefreshReuseGraceSeconds);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}