using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.People.InsuranceProviders;

/// <summary>
/// Acceso a los proveedores de seguros. Los cambios se guardan con la unidad de trabajo.
/// </summary>
public interface IInsuranceProviderRepository : IRepository<InsuranceProvider>
{
    /// <summary>
    /// Busca el proveedor por su id, rastreado para que los cambios del agregado se guarden
    /// con la unidad de trabajo.
    /// </summary>
    /// <param name="id">Id del proveedor.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>El proveedor, o <c>null</c> si no existe.</returns>
    Task<InsuranceProvider?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Indica si ya existe un proveedor, activo o inactivo, con ese nombre. La comparación es
    /// exacta sobre el nombre sin espacios en los extremos: distingue mayúsculas y tildes, la
    /// misma regla que la restricción <c>uq_insurance_provider_name</c>.
    /// </summary>
    /// <param name="name">Nombre a comprobar.</param>
    /// <param name="excludeId">
    /// Id del proveedor que se está actualizando, para no contarlo; <c>null</c> al crear uno.
    /// </param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns><c>true</c> si otro proveedor ya usa ese nombre.</returns>
    Task<bool> ExistsByNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default);
}
