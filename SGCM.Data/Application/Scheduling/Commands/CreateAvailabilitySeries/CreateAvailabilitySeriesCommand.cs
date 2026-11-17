using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;

/// <summary>
/// Crea el horario semanal del médico autenticado. El médico sale de la sesión; la zona horaria
/// y la duración mínima de un bloque, de la configuración de agenda.
/// </summary>
/// <param name="DaysOfWeek">Días en formato ISO 8601: 1 = lunes ... 7 = domingo.</param>
/// <param name="EndDate">Último día de la serie, incluido; <c>null</c> si no tiene fin.</param>
public sealed record CreateAvailabilitySeriesCommand(
    IReadOnlyList<int>? DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly StartDate,
    DateOnly? EndDate) : ICommand<int>;
