using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Scheduling;

namespace SGCM.Data.Application.Scheduling.Commands.RescheduleAvailabilityBlock;

/// <summary>
/// El médico autenticado cambia el horario de uno de sus bloques. La fecha y las horas son locales de la agenda,
/// como al crear un bloque suelto.
/// </summary>
/// <param name="BlockId">Bloque que se reprograma.</param>
/// <param name="Date">Fecha local del horario nuevo.</param>
/// <param name="StartTime">Hora local de inicio, en punto.</param>
/// <param name="EndTime">Hora local de fin.</param>
/// <param name="Reason">
/// Motivo; obligatorio si quedan citas activas fuera del horario nuevo, y extendido si alguna empieza pronto.
/// </param>
public sealed record RescheduleAvailabilityBlockCommand(
    int BlockId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Reason = null) : ICommand<RescheduledAvailabilityBlockDto>
{
    internal bool TryGetUtcRange(BusinessCalendar calendar, out DateTimeOffset startsAt, out DateTimeOffset endsAt)
    {
        endsAt = default;
        return calendar.TryToUtc(Date, StartTime, out startsAt) && calendar.TryToUtc(Date, EndTime, out endsAt);
    }
}
