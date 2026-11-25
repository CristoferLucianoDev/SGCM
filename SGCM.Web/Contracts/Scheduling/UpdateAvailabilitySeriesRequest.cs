namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Recurrencia nueva de una serie propia; aplica desde la primera fecha que la regla de semanas deja reprogramar.</summary>
/// <param name="DaysOfWeek">Días en ISO 8601: 1 = lunes ... 7 = domingo.</param>
/// <param name="StartTime">Hora local de inicio, en punto.</param>
/// <param name="EndTime">Hora local de fin, en punto.</param>
/// <param name="EndDate">Último día de la serie, incluido; <c>null</c> si no tiene fin.</param>
/// <param name="Reason">
/// Motivo; obligatorio si quedan citas activas fuera del horario nuevo, que se cancelan con ese motivo.
/// </param>
public sealed record UpdateAvailabilitySeriesRequest(
    IReadOnlyList<int>? DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly? EndDate,
    string? Reason = null);
