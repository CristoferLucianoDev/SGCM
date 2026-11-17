using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling;

/// <summary>
/// Patrón semanal en hora local: los días que se repite, la hora de inicio y fin en punto, la fecha desde y,
/// opcionalmente, la fecha hasta. No conoce la zona horaria; convertir a UTC le toca a quien la usa.
/// </summary>
public sealed class WeeklyRecurrence
{
    private readonly DayOfWeek[] _daysOfWeek;

    private WeeklyRecurrence(DayOfWeek[] daysOfWeek, TimeOnly startTime, TimeOnly endTime, DateOnly startDate, DateOnly? endDate)
    {
        _daysOfWeek = daysOfWeek;
        StartTime = startTime;
        EndTime = endTime;
        StartDate = startDate;
        EndDate = endDate;
    }

    /// <summary>Días en que se repite, sin duplicados y en orden de lunes a domingo.</summary>
    public IReadOnlyList<DayOfWeek> DaysOfWeek => Array.AsReadOnly(_daysOfWeek);

    public TimeOnly StartTime { get; }

    public TimeOnly EndTime { get; }

    public DateOnly StartDate { get; }

    /// <summary>Último día, incluido; <c>null</c> si no tiene fin.</summary>
    public DateOnly? EndDate { get; }

    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// Valida el patrón: días no vacíos, horas en punto con el fin después del inicio, la duración mínima
    /// de la configuración y la fecha hasta, si existe, no anterior a la fecha desde.
    /// </summary>
    /// <returns>La recurrencia, o el primer error encontrado.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración mínima no es positiva.</exception>
    public static Result<WeeklyRecurrence> Create(
        IEnumerable<DayOfWeek>? daysOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        DateOnly startDate,
        DateOnly? endDate,
        int minimumDurationHours)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumDurationHours);

        var days = daysOfWeek?.Distinct().ToArray() ?? [];
        if (days.Length == 0) return WeeklyRecurrenceErrors.DaysRequired;
        if (days.Any(day => !Enum.IsDefined(day))) return WeeklyRecurrenceErrors.InvalidDayOfWeek;

        if (!HourAlignedInstant.IsOnHour(startTime) || !HourAlignedInstant.IsOnHour(endTime))
            return WeeklyRecurrenceErrors.TimeNotOnHour;
        if (endTime <= startTime) return WeeklyRecurrenceErrors.InvalidTimeRange;
        if (endTime - startTime < TimeSpan.FromHours(minimumDurationHours)) return WeeklyRecurrenceErrors.DurationTooShort;

        if (endDate < startDate) return WeeklyRecurrenceErrors.InvalidDateRange;

        return new WeeklyRecurrence(days.OrderBy(MondayFirst).ToArray(), startTime, endTime, startDate, endDate);
    }

    /// <summary>
    /// Reconstruye una recurrencia que ya se validó al crearse, por ejemplo la de una serie leída de la base.
    /// La duración mínima no se vuelve a exigir, porque la configuración pudo cambiar después.
    /// </summary>
    internal static WeeklyRecurrence Restore(
        IEnumerable<DayOfWeek> daysOfWeek, TimeOnly startTime, TimeOnly endTime, DateOnly startDate, DateOnly? endDate) =>
        new(daysOfWeek.Distinct().OrderBy(MondayFirst).ToArray(), startTime, endTime, startDate, endDate);

    /// <summary>Días en que se repite entre dos fechas, ambas incluidas, recortados a su vigencia.</summary>
    public IEnumerable<DateOnly> DatesBetween(DateOnly from, DateOnly to)
    {
        var first = from > StartDate ? from : StartDate;
        var last = EndDate is { } end && end < to ? end : to;

        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (_daysOfWeek.Contains(date.DayOfWeek)) yield return date;
        }
    }

    /// <summary>
    /// Indica si las dos recurrencias tienen algún día en común, en el tramo en que ambas están vigentes,
    /// con horas que se cruzan. Funciona aunque ninguna tenga fecha de fin: el patrón se repite cada
    /// semana, así que basta revisar una semana del tramo común. Las horas son [inicio, fin): dos horarios
    /// contiguos no se traslapan.
    /// </summary>
    public bool OverlapsWith(WeeklyRecurrence other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!(StartTime < other.EndTime && other.StartTime < EndTime)) return false;

        var first = StartDate > other.StartDate ? StartDate : other.StartDate;
        var last = (EndDate, other.EndDate) switch
        {
            ({ } mine, { } theirs) => mine < theirs ? mine : theirs,
            ({ } mine, null) => mine,
            (null, { } theirs) => theirs,
            _ => first.AddDays(6)
        };
        if (last < first) return false;
        if (last > first.AddDays(6)) last = first.AddDays(6);

        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (_daysOfWeek.Contains(date.DayOfWeek) && other._daysOfWeek.Contains(date.DayOfWeek)) return true;
        }

        return false;
    }

    private static int MondayFirst(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;
}
