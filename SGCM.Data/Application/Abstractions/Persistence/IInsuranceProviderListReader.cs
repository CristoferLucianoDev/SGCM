using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.ListInsuranceProviders;

namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>
/// Lee la lista paginada de proveedores de seguros sin cargar el agregado.
/// </summary>
public interface IInsuranceProviderListReader
{
    /// <summary>
    /// Lista una página de proveedores ordenados por nombre y, a igual nombre, por id.
    /// </summary>
    /// <param name="search">Texto que debe contener el nombre, sin distinguir mayúsculas ni tildes; <c>null</c> no filtra.</param>
    /// <param name="isActive">Estado que se filtra; <c>null</c> incluye activos e inactivos.</param>
    /// <param name="includeStatus">Si es <c>true</c>, cada proveedor informa su estado en <c>IsActive</c>.</param>
    /// <param name="pageNumber">Número de página, desde 1.</param>
    /// <param name="pageSize">Cantidad de proveedores por página.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>La página pedida con el total de proveedores que cumplen el filtro, contado antes de paginar.</returns>
    Task<PagedResult<InsuranceProviderDto>> ListAsync(
        string? search, bool? isActive, bool includeStatus, int pageNumber, int pageSize,
        CancellationToken cancellationToken);
}
