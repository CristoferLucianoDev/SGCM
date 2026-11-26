using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.Scheduling;

/// <summary>
/// Página del historial de citas del paciente.
/// </summary>
/// <param name="Items">Citas de la página, de la más reciente a la más antigua.</param>
/// <param name="PageNumber">Número de la página devuelta.</param>
/// <param name="PageSize">Tamaño de página pedido.</param>
/// <param name="TotalCount">Total de citas que cumplen el filtro, contado antes de paginar.</param>
/// <param name="Message">Aviso cuando no hay ninguna cita; se omite si es <c>null</c>.</param>
public sealed record ListPatientAppointmentsResponse(
    IReadOnlyList<PatientAppointmentResponse> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null);
