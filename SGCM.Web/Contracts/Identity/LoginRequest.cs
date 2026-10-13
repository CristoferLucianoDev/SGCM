namespace SGCM.Web.Contracts.Identity;

/// <summary>
/// Credenciales del inicio de sesión. El dispositivo y la IP no van en el cuerpo; la API
/// los toma de la petición.
/// </summary>
public sealed record LoginRequest(string Email, string Password);
