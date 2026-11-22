namespace SGCM.Web.Contracts.People;

/// <summary>
/// Parámetros de consulta del listado de proveedores de seguros.
/// </summary>
/// <param name="Search">Texto que debe contener el nombre, sin distinguir mayúsculas ni tildes; vacío no filtra.</param>
/// <param name="PageNumber">Número de página, desde 1.</param>
/// <param name="PageSize">Cantidad de proveedores por página, entre 1 y 100.</param>
/// <param name="Status">Estado que se pide; solo el administrador puede ampliarlo más allá de los activos.</param>
public sealed record ListInsuranceProvidersRequest(
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 20,
    InsuranceProviderStatusFilter Status = InsuranceProviderStatusFilter.Active);
