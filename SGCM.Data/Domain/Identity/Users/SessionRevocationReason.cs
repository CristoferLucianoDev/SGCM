namespace SGCM.Data.Domain.Identity.Users;

/// <summary>
/// Motivo por el que se revocó una sesión de usuario.
/// </summary>
public enum SessionRevocationReason
{
    /// <summary>El usuario cerró la sesión desde ese dispositivo.</summary>
    Logout,

    /// <summary>El usuario restableció su contraseña; se cierran sus sesiones abiertas.</summary>
    PasswordReset,

    /// <summary>El usuario desactivó la verificación en dos pasos; se cierran sus sesiones abiertas.</summary>
    TwoFactorDisabled,

    /// <summary>La cuenta se desactivó; ninguna de sus sesiones puede seguir en uso.</summary>
    AccountDeactivated,

    /// <summary>
    /// Se presentó un refresh token ya rotado, lo que se trata como robo del token.
    /// </summary>
    TokenReuse,

    /// <summary>
    /// Se abrió una sesión nueva con el máximo de sesiones activas alcanzado; se revoca
    /// la usada hace más tiempo.
    /// </summary>
    SessionLimit
}
