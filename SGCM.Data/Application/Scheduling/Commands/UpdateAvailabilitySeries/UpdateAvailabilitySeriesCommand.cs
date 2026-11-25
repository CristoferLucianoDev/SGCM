using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Commands.UpdateAvailabilitySeries;

/// <summary>
/// El médico autenticado cambia la recurrencia de una de sus series. El cambio aplica desde la primera fecha que la
/// regla de semanas deja reprogramar; la semana actual no cambia.
/// </summary>
/// <param name="SeriesId">Serie que se edita.</param>
/// <param name="DaysOfWeek">Días nuevos en ISO 8601: 1 = lunes ... 7 = domingo.</param>
/// <param name="StartTime">Hora local de inicio, en punto.</param>
/// <param name="EndTime">Hora local de fin, en punto.</param>
/// <param name="EndDate">Último día de la serie, incluido; <c>null</c> si no tiene fin.</param>
/// <param name="Reason">
/// Motivo; obligatorio si quedan citas activas fuera del horario nuevo, y extendido si alguna empieza pronto.
/// </param>
public sealed record UpdateAvailabilitySeriesCommand(
    int SeriesId,
    IReadOnlyList<int>? DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly? EndDate,
    string? Reason = null) : ICommand<UpdatedAvailabilitySeriesDto>;
