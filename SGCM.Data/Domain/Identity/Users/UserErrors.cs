using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Identity.Users;

/// <summary>
/// Errores de negocio del agregado <see cref="User"/>.
/// </summary>
public static class UserErrors
{
    private const string SessionInvalidDescription = "La sesión no es válida. Inicia sesión de nuevo.";

    /// <summary>
    /// La contraseña no alcanza la longitud mínima.
    /// </summary>
    public static readonly Error PasswordTooShort = Error.Validation(
        code: "User.PasswordTooShort",
        description: $"La contraseña debe tener al menos {Password.MinLength} caracteres."
    );

    /// <summary>
    /// La contraseña supera la longitud máxima.
    /// </summary>
    public static readonly Error PasswordTooLong = Error.Validation(
        code: "User.PasswordTooLong",
        description: $"La contraseña no puede superar {Password.MaxLength} caracteres."
    );

    /// <summary>
    /// Ya existe una cuenta confirmada con ese correo.
    /// </summary>
    public static readonly Error EmailAlreadyInUse = Error.Conflict(
        code: "User.EmailAlreadyInUse",
        description: "El correo ya está en uso."
    );

    /// <summary>
    /// La cuenta está desactivada y no admite la operación.
    /// </summary>
    public static readonly Error AccountInactive = Error.Forbidden(
        code: "User.AccountInactive",
        description: "La cuenta está desactivada."
    );

    /// <summary>
    /// El token no existe, ya se usó o fue reemplazado por otro.
    /// </summary>
    public static readonly Error TokenInvalid = Error.Validation(
        code: "User.TokenInvalid",
        description: "El enlace no es válido o ya fue usado."
    );

    /// <summary>
    /// El token existe y no se usó, pero pasó su fecha de vencimiento.
    /// </summary>
    public static readonly Error TokenExpired = Error.Validation(
        code: "User.TokenExpired",
        description: "El enlace venció."
    );

    /// <summary>
    /// El registro pendiente ya no tiene ningún token de confirmación vigente,
    /// así que se trata como inexistente.
    /// </summary>
    public static readonly Error RegistrationExpired = Error.Validation(
        code: "User.RegistrationExpired",
        description: "El enlace venció. Debes registrarte de nuevo."
    );

    /// <summary>
    /// Se pidió un reenvío del correo de confirmación para una cuenta que ya confirmó su correo.
    /// </summary>
    public static readonly Error EmailAlreadyConfirmed = Error.Conflict(
        code: "User.EmailAlreadyConfirmed",
        description: "Tu correo ya está confirmado. Inicia sesión."
    );

    /// <summary>
    /// Se pidió un reenvío del correo de confirmación antes de que pase la espera
    /// mínima desde el último enlace enviado.
    /// </summary>
    public static readonly Error ResendTooSoon = Error.Conflict(
        code: "User.ResendTooSoon",
        description: "Debes esperar antes de pedir otro correo."
    );

    /// <summary>
    /// Se intentó abrir una sesión para una cuenta que no confirmó su correo.
    /// </summary>
    public static readonly Error EmailNotConfirmed = Error.Forbidden(
        code: "User.EmailNotConfirmed",
        description: "Debes confirmar tu correo antes de iniciar sesión."
    );

    /// <summary>
    /// El correo o la contraseña no son correctos. Es la misma respuesta para correo
    /// inexistente, contraseña incorrecta, cuenta pendiente o inactiva, para no revelar
    /// qué cuentas existen.
    /// </summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        code: "User.InvalidCredentials",
        description: "Correo o contraseña incorrectos."
    );

    /// <summary>
    /// Se superó el límite de intentos de inicio de sesión.
    /// </summary>
    public static readonly Error TooManyLoginAttempts = Error.TooManyRequests(
        code: "User.TooManyLoginAttempts",
        description: "Demasiados intentos. Intenta de nuevo más tarde."
    );

    /// <summary>
    /// Se superó el límite de solicitudes de recuperación de contraseña para un correo.
    /// </summary>
    public static readonly Error TooManyPasswordResetRequests = Error.TooManyRequests(
        code: "User.TooManyPasswordResetRequests",
        description: "Demasiadas solicitudes de recuperación. Intenta de nuevo más tarde."
    );

    /// <summary>
    /// La sesión no se puede renovar. Es la misma respuesta para un hash que no coincide con
    /// ninguna sesión, una sesión revocada o vencida, una cuenta inactiva y el hash anterior
    /// de una sesión inactiva o dentro del margen de gracia.
    /// </summary>
    public static readonly Error SessionInvalid = Error.Unauthorized(
        code: "User.SessionInvalid",
        description: SessionInvalidDescription
    );

    /// <summary>
    /// Se presentó el hash anterior inmediato de una sesión activa fuera del margen de gracia:
    /// se asume robo y se revocan todas las sesiones activas del usuario. Su descripción es la
    /// de <see cref="SessionInvalid"/> para no revelar al cliente que se detectó la reutilización.
    /// </summary>
    public static readonly Error RefreshTokenReused = Error.Unauthorized(
        code: "User.RefreshTokenReused",
        description: SessionInvalidDescription
    );
}
