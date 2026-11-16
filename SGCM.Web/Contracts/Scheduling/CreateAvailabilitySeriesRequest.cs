namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Horario semanal que declara el médico autenticado.</summary>
/// <param name="DaysOfWeek">Días en formato ISO 8601: 1 = lunes ... 7 = domingo.</param>
/// <param name="StartTime">Hora de inicio en punto, por ejemplo "08:00:00".</param>
/// <param name="EndTime">Hora de fin en punto, por ejemplo "14:00:00".</param>
/// <param name="StartDate">Primer día de la serie; no puede ser pasado.</param>
/// <param name="EndDate">Último día de la serie, incluido; se omite si no tiene fin.</param>
public sealed record CreateAvailabilitySeriesRequest(
    IReadOnlyList<int>? DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly StartDate,
    DateOnly? EndDate);
