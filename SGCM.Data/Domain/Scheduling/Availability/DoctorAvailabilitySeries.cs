using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Availability.Events;

namespace SGCM.Data.Domain.Scheduling.Availability;

/// <summary>
/// Horario semanal recurrente del médico: los días que atiende, la hora de inicio y fin en punto,
/// la fecha desde y, opcionalmente, la fecha hasta. A partir de ella se genera un bloque por día.
/// </summary>
public sealed class DoctorAvailabilitySeries : ActivatableAggregateRoot
{
    // Días en formato ISO 8601 (1 = lunes ... 7 = domingo), ordenados y sin repetir, como los guarda la base.
    private short[] _isoDaysOfWeek = [];

    public int DoctorId { get; private set; }

    /// <summary>Días en que se repite, sin duplicados y en orden de lunes a domingo.</summary>
    public IReadOnlyList<DayOfWeek> DaysOfWeek => Array.AsReadOnly(_isoDaysOfWeek.Select(FromIsoDayNumber).ToArray());

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>Último día de la serie, incluido; <c>null</c> si no tiene fin.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>Zona horaria en que se interpretan las horas de la serie.</summary>
    public string TimeZone { get; private set; } = null!;

    protected override string EntityName => nameof(DoctorAvailabilitySeries);

    private DoctorAvailabilitySeries()
    {
    }

    /// <summary>
    /// Crea una serie activa y emite <see cref="DoctorAvailabilitySeriesCreatedEvent"/>.
    /// Que no se traslape con otro horario del médico lo comprueba aplicación contra la base.
    /// </summary>
    /// <param name="minimumDurationHours">Duración mínima de un bloque, en horas, según la configuración.</param>
    /// <returns>La serie, o el primer error de validación encontrado.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración mínima no es positiva.</exception>
    public static Result<DoctorAvailabilitySeries> Create(
        int doctorId,
        IEnumerable<DayOfWeek>? daysOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        DateOnly startDate,
        DateOnly? endDate,
        string? timeZone,
        int minimumDurationHours)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumDurationHours);

        if (doctorId <= 0) return DoctorAvailabilitySeriesErrors.InvalidDoctor;

        var days = daysOfWeek?.Distinct().ToList() ?? [];
        if (days.Count == 0) return DoctorAvailabilitySeriesErrors.DaysRequired;
        if (days.Any(day => !Enum.IsDefined(day))) return DoctorAvailabilitySeriesErrors.InvalidDayOfWeek;

        if (!HourAlignedInstant.IsOnHour(startTime) || !HourAlignedInstant.IsOnHour(endTime))
            return DoctorAvailabilitySeriesErrors.TimeNotOnHour;
        if (endTime <= startTime) return DoctorAvailabilitySeriesErrors.InvalidTimeRange;
        if (endTime - startTime < TimeSpan.FromHours(minimumDurationHours))
            return DoctorAvailabilitySeriesErrors.DurationTooShort;

        if (endDate < startDate) return DoctorAvailabilitySeriesErrors.InvalidDateRange;

        var calendar = BusinessCalendar.Create(timeZone);
        if (calendar.IsFailure) return DoctorAvailabilitySeriesErrors.InvalidTimeZone;

        var series = new DoctorAvailabilitySeries
        {
            DoctorId = doctorId,
            _isoDaysOfWeek = days.Select(day => (short)IsoDayNumber(day)).Order().ToArray(),
            StartTime = startTime,
            EndTime = endTime,
            StartDate = startDate,
            EndDate = endDate,
            TimeZone = calendar.Value.TimeZoneId
        };

        series.Raise(new DoctorAvailabilitySeriesCreatedEvent(series));
        return series;
    }

    /// <summary>
    /// Días en que se repite la serie entre dos fechas, ambas incluidas, recortados a su vigencia.
    /// Las horas se interpretan en <see cref="TimeZone"/> y se devuelven en UTC.
    /// Un día cuya hora local no existe por un cambio de horario se omite.
    /// </summary>
    /// <returns>Las ocurrencias en orden cronológico; vacío si el rango no toca la vigencia.</returns>
    public IReadOnlyList<AvailabilityOccurrence> GetOccurrences(DateOnly from, DateOnly to)
    {
        var first = from > StartDate ? from : StartDate;
        var last = EndDate is { } end && end < to ? end : to;
        if (last < first) return [];

        var days = DaysOfWeek;
        var calendar = Calendar();
        var occurrences = new List<AvailabilityOccurrence>();

        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (!days.Contains(date.DayOfWeek)) continue;

            if (!calendar.TryToUtc(date, StartTime, out var startsAt) || !calendar.TryToUtc(date, EndTime, out var endsAt))
                continue;

            occurrences.Add(new AvailabilityOccurrence(date, startsAt, endsAt));
        }

        return occurrences;
    }

    /// <summary>
    /// Indica si alguna ocurrencia de esta serie se cruza con alguna de otra serie. Basta revisar
    /// una semana del tramo en que ambas están vigentes, porque el patrón se repite cada 7 días.
    /// </summary>
    public bool OverlapsWith(DoctorAvailabilitySeries other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var first = StartDate > other.StartDate ? StartDate : other.StartDate;
        var last = (EndDate, other.EndDate) switch
        {
            ({ } mine, { } theirs) => mine < theirs ? mine : theirs,
            ({ } mine, null) => mine,
            (null, { } theirs) => theirs,
            _ => first.AddDays(7)
        };
        if (last < first) return false;
        if (last > first.AddDays(7)) last = first.AddDays(7);

        // Un día de margen por si las zonas horarias de las series difieren.
        var mineOccurrences = GetOccurrences(first.AddDays(-1), last.AddDays(1));
        var theirOccurrences = other.GetOccurrences(first.AddDays(-1), last.AddDays(1));
        return mineOccurrences.Any(occurrence =>
            theirOccurrences.Any(their => Overlaps(occurrence, their.StartsAt, their.EndsAt)));
    }

    /// <summary>Indica si alguna ocurrencia de la serie se cruza con el intervalo [inicio, fin) dado.</summary>
    public bool OverlapsWith(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        // Un día de margen a cada lado cubre cualquier diferencia entre UTC y la zona de la serie.
        var from = DateOnly.FromDateTime(startsAt.UtcDateTime).AddDays(-1);
        var to = DateOnly.FromDateTime(endsAt.UtcDateTime).AddDays(1);

        return GetOccurrences(from, to).Any(occurrence => Overlaps(occurrence, startsAt, endsAt));
    }

    /// <summary>Fecha local de un instante en la zona horaria de la serie.</summary>
    public DateOnly LocalDateOf(DateTimeOffset instant) => Calendar().ToLocalDate(instant);

    /// <summary>
    /// Convierte un número ISO 8601 (1 = lunes ... 7 = domingo) en el día de la semana.
    /// </summary>
    /// <returns><c>false</c> si el número no está entre 1 y 7.</returns>
    public static bool TryFromIsoDayNumber(int isoDay, out DayOfWeek day)
    {
        day = isoDay == 7 ? DayOfWeek.Sunday : (DayOfWeek)isoDay;
        return isoDay is >= 1 and <= 7;
    }

    /// <summary>Número ISO 8601 del día: 1 = lunes ... 7 = domingo, como lo guarda la base.</summary>
    public static int IsoDayNumber(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static DayOfWeek FromIsoDayNumber(short isoDay) => isoDay == 7 ? DayOfWeek.Sunday : (DayOfWeek)isoDay;

    // Los intervalos son [inicio, fin): dos bloques contiguos no se traslapan, igual que en la base.
    private static bool Overlaps(AvailabilityOccurrence occurrence, DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        occurrence.Range.Overlaps(startsAt, endsAt);

    // La zona se validó al crear la serie.
    private BusinessCalendar Calendar() => BusinessCalendar.ForTimeZone(TimeZone);
}
