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

        if (!IsOnHour(startTime) || !IsOnHour(endTime)) return DoctorAvailabilitySeriesErrors.TimeNotOnHour;
        if (endTime <= startTime) return DoctorAvailabilitySeriesErrors.InvalidTimeRange;
        if (endTime - startTime < TimeSpan.FromHours(minimumDurationHours))
            return DoctorAvailabilitySeriesErrors.DurationTooShort;

        if (endDate < startDate) return DoctorAvailabilitySeriesErrors.InvalidDateRange;

        var trimmedTimeZone = timeZone?.Trim();
        if (FindTimeZone(trimmedTimeZone) is null) return DoctorAvailabilitySeriesErrors.InvalidTimeZone;

        var series = new DoctorAvailabilitySeries
        {
            DoctorId = doctorId,
            _isoDaysOfWeek = days.Select(day => (short)IsoDayNumber(day)).Order().ToArray(),
            StartTime = startTime,
            EndTime = endTime,
            StartDate = startDate,
            EndDate = endDate,
            TimeZone = trimmedTimeZone!
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
        var zone = FindTimeZone(TimeZone)
            ?? throw new InvalidOperationException($"La zona horaria '{TimeZone}' de la serie no existe.");
        var occurrences = new List<AvailabilityOccurrence>();

        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (!days.Contains(date.DayOfWeek)) continue;

            var localStart = date.ToDateTime(StartTime, DateTimeKind.Unspecified);
            var localEnd = date.ToDateTime(EndTime, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(localStart) || zone.IsInvalidTime(localEnd)) continue;

            occurrences.Add(new AvailabilityOccurrence(
                date,
                new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone)),
                new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, zone))));
        }

        return occurrences;
    }

    /// <summary>Número ISO 8601 del día: 1 = lunes ... 7 = domingo, como lo guarda la base.</summary>
    public static int IsoDayNumber(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static DayOfWeek FromIsoDayNumber(short isoDay) => isoDay == 7 ? DayOfWeek.Sunday : (DayOfWeek)isoDay;

    private static bool IsOnHour(TimeOnly time) => time.Ticks % TimeSpan.TicksPerHour == 0;

    private static TimeZoneInfo? FindTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }
}
