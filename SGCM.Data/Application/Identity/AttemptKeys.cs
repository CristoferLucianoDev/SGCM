using SGCM.Data.Domain.Common.ValueObjects;

namespace SGCM.Data.Application.Identity;

/// <summary>
/// Claves de <see cref="Abstractions.Security.IAttemptLimiter"/> por operación. Viven en un
/// solo lugar porque un caso de uso reinicia el contador que registra otro.
/// </summary>
internal static class AttemptKeys
{
    private const string LoginPrefix = "login:";
    private const string PasswordResetPrefix = "password-reset:";

    /// <summary>
    /// Clave de los intentos de inicio de sesión con el correo indicado.
    /// </summary>
    /// <param name="email">Correo ya normalizado por el value object.</param>
    /// <returns>La clave del contador.</returns>
    public static string Login(Email email) => LoginPrefix + email.Value;

    /// <summary>
    /// Clave de las solicitudes de recuperación de contraseña del correo indicado.
    /// </summary>
    /// <param name="email">Correo ya normalizado por el value object.</param>
    /// <returns>La clave del contador.</returns>
    public static string PasswordReset(Email email) => PasswordResetPrefix + email.Value;
}
