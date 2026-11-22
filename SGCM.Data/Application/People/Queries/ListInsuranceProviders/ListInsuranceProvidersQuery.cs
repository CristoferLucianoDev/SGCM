using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Application.People.Queries.ListInsuranceProviders;

/// <summary>
/// Lista paginada de proveedores de seguros ordenada por nombre. Omitir <paramref name="IsActive"/> selecciona
/// los activos; <c>null</c> solicita explícitamente todos los estados. Solo el administrador puede cambiar el
/// estado: para los demás siempre se consultan los activos.
/// </summary>
/// <param name="Search">Texto que debe contener el nombre, sin distinguir mayúsculas ni tildes; <c>null</c> no filtra.</param>
/// <param name="IsActive">Estado que se consulta; <c>null</c> incluye activos e inactivos.</param>
/// <param name="PageNumber">Número de página, desde 1.</param>
/// <param name="PageSize">Cantidad de proveedores por página, entre 1 y 100.</param>
public sealed record ListInsuranceProvidersQuery(
    string? Search = null, bool? IsActive = true, int PageNumber = 1, int PageSize = 20)
    : IQuery<PagedResult<InsuranceProviderDto>>;
