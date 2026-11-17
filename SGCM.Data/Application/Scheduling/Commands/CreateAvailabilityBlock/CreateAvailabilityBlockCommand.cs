using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Scheduling;

namespace SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;

/// <summary>
/// Crea un bloque suelto, sin serie, para el médico autenticado. La fecha y las horas son locales y se
/// interpretan en la zona horaria de la agenda; el médico sale de la sesión.
/// </summary>
public sealed record CreateAvailabilityBlockCommand(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime) : ICommand<int>
{
    /// <summary>Inicio y fin en UTC, o <c>false</c> si una de las horas no existe por el horario de verano.</summary>
    internal bool TryGetUtcRange(BusinessCalendar calendar, out DateTimeOffset startsAt, out DateTimeOffset endsAt)
    {
        endsAt = default;
        return calendar.TryToUtc(Date, StartTime, out startsAt) && calendar.TryToUtc(Date, EndTime, out endsAt);
    }
}
