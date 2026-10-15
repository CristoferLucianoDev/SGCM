namespace SGCM.Web.Contracts.Identity;

/// <summary>
/// Correo de la cuenta a la que se envía el enlace para restablecer la contraseña.
/// </summary>
public sealed record ForgotPasswordRequest(string Email);