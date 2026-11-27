using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Reporte de citas: los totales por estado y una página de filas.</summary>
/// <param name="Totals">Total por estado sobre todo el reporte, siempre con los cinco estados.</param>
/// <param name="Items">Filas de la página, en el orden pedido.</param>
/// <param name="PageNumber">Número de la página devuelta.</param>
/// <param name="PageSize">Tamaño de página pedido.</param>
/// <param name="TotalCount">Total de filas del reporte, contado antes de paginar.</param>
/// <param name="Message">Aviso cuando el reporte no tiene datos; se omite si es <c>null</c>.</param>
public sealed record AppointmentReportResponse(
    IReadOnlyList<AppointmentReportStatusTotalResponse> Totals,
    IReadOnlyList<AppointmentReportRowResponse> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null);
