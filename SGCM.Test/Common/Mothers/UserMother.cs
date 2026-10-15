using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="User"/>: crea usuarios en estados conocidos con datos
/// fijos, para que las pruebas solo declaren lo que les importa.
/// </summary>
public static class UserMother
{
    /// <summary>Momento fijo que usan las pruebas como "ahora".</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 6, 15, 0, 0, TimeSpan.Zero);

    /// <summary>Vigencia del token de confirmación de los usuarios creados.</summary>
    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Correo de los usuarios creados.</summary>
    public static readonly Email DefaultEmail = Email.Create("paciente@correo.com").Value;

    /// <summary>Hash de contraseña de los usuarios creados.</summary>
    public const string DefaultPasswordHash = "hash-de-contrasena";

    /// <summary>Token de confirmación en claro del registro inicial.</summary>
    public const string DefaultRawToken = "raw-token";

    /// <summary>Hash del token de confirmación del registro inicial.</summary>
    public const string DefaultTokenHash = "hash-token";

    /// <summary>
    /// Paciente recién registrado, con la cuenta pendiente de confirmar.
    /// </summary>
    /// <param name="email">Correo del usuario; si es <c>null</c>, <see cref="DefaultEmail"/>.</param>
    /// <param name="passwordHash">Hash de la contraseña; si es <c>null</c>, <see cref="DefaultPasswordHash"/>.</param>
    public static User Pending(Email? email = null, string? passwordHash = null) =>
        User.RegisterPending(
            email ?? DefaultEmail,
            passwordHash ?? DefaultPasswordHash,
            DefaultRawToken,
            DefaultTokenHash,
            Now,
            ConfirmationLifetime);

    /// <summary>
    /// Paciente con el correo ya confirmado. Usa reflexión porque el agregado todavía
    /// no expone la operación de confirmar; cuando exista, debe reemplazarse por ella.
    /// </summary>
    /// <param name="email">Correo del usuario; si es <c>null</c>, <see cref="DefaultEmail"/>.</param>
    /// <param name="passwordHash">Hash de la contraseña; si es <c>null</c>, <see cref="DefaultPasswordHash"/>.</param>
    public static User Confirmed(Email? email = null, string? passwordHash = null)
    {
        var user = Pending(email, passwordHash);
        typeof(User).GetProperty(nameof(User.IsEmailConfirmed))!.SetValue(user, true);
        return user;
    }

    /// <summary>
    /// Usuario confirmado con el rol y la contraseña temporal indicados. Usa reflexión porque
    /// el agregado todavía no expone cómo crear médicos ni administradores; cuando exista,
    /// debe reemplazarse por esa operación.
    /// </summary>
    /// <param name="role">Rol del usuario.</param>
    /// <param name="mustChangePassword">Indica si la contraseña es temporal.</param>
    /// <param name="email">Correo del usuario; si es <c>null</c>, <see cref="DefaultEmail"/>.</param>
    /// <param name="passwordHash">
    /// Hash de la contraseña; si es <c>null</c>, <see cref="DefaultPasswordHash"/>, con el que
    /// el usuario no puede iniciar sesión.
    /// </param>
    public static User WithRole(
        UserRole role,
        bool mustChangePassword = false,
        Email? email = null,
        string? passwordHash = null)
    {
        var user = Confirmed(email, passwordHash);
        typeof(User).GetProperty(nameof(User.Role))!.SetValue(user, role);
        typeof(User).GetProperty(nameof(User.MustChangePassword))!.SetValue(user, mustChangePassword);
        return user;
    }
}
