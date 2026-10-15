using System.Globalization;
using SGCM.Data.Application.Abstractions.Notifications;

namespace SGCM.Data.Application.Identity.Notifications;

/// <summary>
/// Correo con el enlace para restablecer la contraseña de una cuenta.
/// </summary>
/// <param name="Recipient">Destinatario del correo.</param>
/// <param name="ResetUrl">Enlace completo, con el token en claro, para restablecer la contraseña.</param>
/// <param name="ExpirationMinutes">Vigencia del enlace, en minutos.</param>
public sealed record PasswordResetNotification(
    Recipient Recipient,
    string ResetUrl,
    int ExpirationMinutes) : Notification(Recipient)
{
    /// <summary>Clave de la plantilla <c>password-reset.html</c>.</summary>
    public override string TemplateKey => "password-reset";

    /// <summary>
    /// Valores que reemplazan las variables de la plantilla.
    /// </summary>
    /// <returns>El enlace y su vigencia, con las claves que usa la plantilla.</returns>
    public override IReadOnlyDictionary<string, string> GetValues() =>
        new Dictionary<string, string>
        {
            ["ResetUrl"] = ResetUrl,
            ["ExpirationMinutes"] = ExpirationMinutes.ToString(CultureInfo.InvariantCulture)
        };

    /// <summary>
    /// Representación que oculta el enlace, porque lleva el token en claro y con él se puede
    /// cambiar la contraseña si la notificación termina en un log.
    /// </summary>
    public override string ToString() =>
        $"{nameof(PasswordResetNotification)} {{ Recipient = {Recipient}, ResetUrl = ********, ExpirationMinutes = {ExpirationMinutes} }}";
}