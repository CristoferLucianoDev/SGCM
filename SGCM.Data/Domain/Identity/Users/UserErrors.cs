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
}
