namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>
/// Consulta el estado de los proveedores de seguros desde otros agregados.
/// </summary>
public interface IInsuranceProviderReader
{
    /// <summary>
    /// Indica si el proveedor existe y está activo.
    /// </summary>
    /// <param name="insuranceProviderId">Id del proveedor.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns><c>true</c> si existe y está activo.</returns>
    Task<bool> IsActiveAsync(int insuranceProviderId, CancellationToken cancellationToken);
}
