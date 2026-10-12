namespace SGCM.Web.Contracts.Identity;

/// <summary>
/// Correo del registro pendiente al que se reenvía el enlace de confirmación.
/// </summary>
public sealed record ResendConfirmationEmailRequest(string Email);
