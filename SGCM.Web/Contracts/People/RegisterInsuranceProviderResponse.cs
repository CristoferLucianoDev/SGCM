namespace SGCM.Web.Contracts.People;

/// <summary>
/// Resultado del registro de un proveedor de seguros.
/// </summary>
/// <param name="Id">Id del proveedor creado.</param>
public sealed record RegisterInsuranceProviderResponse(int Id);
