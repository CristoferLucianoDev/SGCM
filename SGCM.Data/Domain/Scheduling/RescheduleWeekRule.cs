using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling;

/// <summary>
/// Regla de semanas para reprogramar, con semanas de lunes a domingo en la zona de la agenda: la semana actual no se
/// reprograma, la siguiente solo de lunes a miércoles y las posteriores en cualquier momento. Es el único lugar de
/// la regla: la usan la reprogramación de un bloque y la edición de toda la serie.
/// </summary>
public static class RescheduleWeekRule
{
    // Lunes, martes y miércoles: los días en que todavía se puede reprogramar la semana siguiente.
    private const int LastDayToRescheduleNextWeek = 2;

    /// <summary>Comprueba si un horario de ese día local se puede reprogramar ahora.</summary>
    /// <param name="localDate">Día local del horario, el actual o el nuevo.</param>
    /// <param name="now">Instante actual.</param>
    /// <param name="calendar">Calendario de la agenda.</param>
    /// <returns>
    /// <see cref="Error.None"/>, <see cref="SchedulingErrors.CurrentWeekLocked"/> o
    /// <see cref="SchedulingErrors.NextWeekLocked"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Si el calendario es nulo.</exception>
    public static Error Check(DateOnly localDate, DateTimeOffset now, BusinessCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var nextWeek = calendar.StartOfNextWeek(now);
        if (localDate < nextWeek) return SchedulingErrors.CurrentWeekLocked;
        if (localDate < nextWeek.AddDays(7) && !CanRescheduleNextWeek(now, calendar)) return SchedulingErrors.NextWeekLocked;

        return Error.None;
    }

    /// <summary>
    /// Primer día local que se puede reprogramar ahora: el lunes de la semana siguiente de lunes a miércoles, y el de
    /// la semana posterior de jueves a domingo. Sirve para aplicar un cambio de la serie sin tocar las semanas bloqueadas.
    /// </summary>
    /// <exception cref="ArgumentNullException">Si el calendario es nulo.</exception>
    public static DateOnly FirstReschedulableDate(DateTimeOffset now, BusinessCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var nextWeek = calendar.StartOfNextWeek(now);
        return CanRescheduleNextWeek(now, calendar) ? nextWeek : nextWeek.AddDays(7);
    }

    private static bool CanRescheduleNextWeek(DateTimeOffset now, BusinessCalendar calendar) =>
        (calendar.Today(now).DayNumber - calendar.StartOfCurrentWeek(now).DayNumber) <= LastDayToRescheduleNextWeek;
}
