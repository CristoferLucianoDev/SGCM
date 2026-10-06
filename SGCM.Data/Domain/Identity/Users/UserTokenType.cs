namespace SGCM.Data.Domain.Identity.Users;

/// <summary>
/// Propósito de un <see cref="UserToken"/>. Los valores numéricos son explícitos porque se persisten.
/// </summary>
public enum UserTokenType
{
    /// <summary>Confirma el correo de una cuenta pendiente.</summary>
    EmailConfirmation = 1,

    /// <summary>Permite restablecer una contraseña olvidada.</summary>
    PasswordReset = 2
}
