using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Identity.Users;

/// <summary>
/// Errores de negocio del agregado <see cref="User"/>.
/// </summary>
public static class UserErrors
{
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
}
