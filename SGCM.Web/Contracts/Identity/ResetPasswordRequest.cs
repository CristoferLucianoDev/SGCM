namespace SGCM.Web.Contracts.Identity;

/// <summary>
/// Token en claro recibido en el enlace del correo de recuperación y la contraseña nueva.
/// </summary>
public sealed record ResetPasswordRequest(string Token, string NewPassword)
{
    /// <summary>
    /// Oculta el token y la contraseña, para que no se filtren si la petición termina en un log.
    /// </summary>
    public override string ToString() =>
        $"{nameof(ResetPasswordRequest)} {{ Token = ********, NewPassword = ******** }}";
}