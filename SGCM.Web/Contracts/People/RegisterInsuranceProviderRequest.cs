namespace SGCM.Web.Contracts.People;

/// <summary>
/// Datos para registrar un proveedor de seguros. El sitio web es opcional.
/// </summary>
public sealed record RegisterInsuranceProviderRequest(
    string Name,
    string ContactPhone,
    string ContactEmail,
    string Address,
    string? Website);
