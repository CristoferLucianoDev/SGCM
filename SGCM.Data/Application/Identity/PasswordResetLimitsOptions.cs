using Microsoft.Extensions.Options;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Application.Identity;

/// <summary>
/// Límites de las solicitudes de recuperación de contraseña.
/// </summary>
public sealed class PasswordResetLimitsOptions
{
    /// <summary>Sección de configuración de estas opciones.</summary>
    public const string SectionName = "PasswordResetLimits";

    /// <summary>Máximo de solicitudes por correo dentro de la ventana.</summary>
    public int MaxRequestsPerEmail { get; set; }

    /// <summary>Duración de la ventana del límite por correo, en minutos.</summary>
    public int EmailWindowMinutes { get; set; }

    /// <summary>
    /// Máximo de peticiones por IP dentro de la ventana. Lo aplica el rate limiting del
    /// endpoint, no el comando.
    /// </summary>
    public int MaxRequestsPerIp { get; set; }

    /// <summary>
    /// Duración de la ventana del límite por IP, en segundos. Lo aplica el rate limiting del
    /// endpoint, no el comando.
    /// </summary>
    public int IpWindowSeconds { get; set; }
}

/// <summary>
/// Valida al arrancar que los límites sean positivos y que las ventanas no pasen de un día.
/// </summary>
public sealed class PasswordResetLimitsOptionsValidator : IValidateOptions<PasswordResetLimitsOptions>
{
    public ValidateOptionsResult Validate(string? name, PasswordResetLimitsOptions options)
    {
        var errors = new List<string>();

        OptionsRules.Positive(errors, "PasswordResetLimits:MaxRequestsPerEmail", options.MaxRequestsPerEmail);
        OptionsRules.Positive(errors, "PasswordResetLimits:EmailWindowMinutes", options.EmailWindowMinutes);
        OptionsRules.Positive(errors, "PasswordResetLimits:MaxRequestsPerIp", options.MaxRequestsPerIp);
        OptionsRules.Positive(errors, "PasswordResetLimits:IpWindowSeconds", options.IpWindowSeconds);
        OptionsRules.AtMost(
            errors, "PasswordResetLimits:EmailWindowMinutes", options.EmailWindowMinutes, LoginLimitsOptionsValidator.MaxEmailWindowMinutes);
        OptionsRules.AtMost(
            errors, "PasswordResetLimits:IpWindowSeconds", options.IpWindowSeconds, LoginLimitsOptionsValidator.MaxIpWindowSeconds);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
