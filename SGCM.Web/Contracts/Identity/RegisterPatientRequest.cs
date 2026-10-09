namespace SGCM.Web.Contracts.Identity;

/// <summary>
/// Datos del registro público de un paciente. Cualquier otro campo del JSON, como un rol, se ignora.
/// </summary>
public sealed record RegisterPatientRequest(string Email, string Password);