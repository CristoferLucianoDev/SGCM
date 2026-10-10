namespace SGCM.Web.Contracts.Identity;

/// <summary>
/// Token en claro recibido en el enlace del correo de confirmación.
/// </summary>
public sealed record ConfirmEmailRequest(string Token);
