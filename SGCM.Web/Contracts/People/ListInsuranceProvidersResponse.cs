using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.People;

/// <summary>
/// Página de proveedores de seguros.
/// </summary>
/// <param name="Items">Proveedores de la página, ordenados por nombre.</param>
/// <param name="PageNumber">Número de la página devuelta.</param>
/// <param name="PageSize">Tamaño de página pedido.</param>
/// <param name="TotalCount">Total de proveedores que cumplen el filtro, contado antes de paginar.</param>
/// <param name="Message">Aviso cuando no hay ningún proveedor; se omite si es <c>null</c>.</param>
public sealed record ListInsuranceProvidersResponse(
    IReadOnlyList<InsuranceProviderResponse> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null);
