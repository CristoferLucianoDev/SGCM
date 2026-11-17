using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling;

/// <summary>
/// Convierte entre UTC y la zona horaria de la agenda. Responde las preguntas de calendario que comparten
/// la antelación de días, las reglas de reprogramación por semana, los recordatorios y los reportes:
/// la fecha local de hoy, el lunes de la semana actual y de la siguiente, y el día de la semana.
/// Las semanas empiezan el lunes, como en ISO 8601.
/// </summary>
public sealed class BusinessCalendar
{
    private readonly TimeZoneInfo _zone;

    private BusinessCalendar(TimeZoneInfo zone, string timeZoneId)
    {
        _zone = zone;
        TimeZoneId = timeZoneId;
    }

    /// <summary>Id de la zona horaria, por ejemplo America/Santo_Domingo.</summary>
    public string TimeZoneId { get; }

    /// <returns>El calendario, o <see cref="SchedulingErrors.InvalidTimeZone"/> si la zona no existe.</returns>
    public static Result<BusinessCalendar> Create(string? timeZoneId)
    {
        var id = timeZoneId?.Trim();
        if (string.IsNullOrEmpty(id)) return SchedulingErrors.InvalidTimeZone;

        try
        {
            return new BusinessCalendar(TimeZoneInfo.FindSystemTimeZoneById(id), id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return SchedulingErrors.InvalidTimeZone;
        }
    }

    /// <summary>Calendario de una zona que ya se validó, como la de la configuración o la de una serie guardada.</summary>
    /// <exception cref="ArgumentException">Si la zona no existe.</exception>
    public static BusinessCalendar ForTimeZone(string timeZoneId)
    {
        var calendar = Create(timeZoneId);
        return calendar.IsSuccess
            ? calendar.Value
            : throw new ArgumentException($"La zona horaria '{timeZoneId}' no existe.", nameof(timeZoneId));
    }

    /// <summary>El mismo instante con el desfase de la zona horaria.</summary>
    public DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, _zone);

    /// <summary>Fecha local de un instante.</summary>
    public DateOnly ToLocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant).DateTime);

    /// <summary>Fecha local de hoy: a las 21:00 de Santo Domingo ya es mañana en UTC, pero aquí sigue siendo hoy.</summary>
    public DateOnly Today(DateTimeOffset utcNow) => ToLocalDate(utcNow);

    /// <summary>Día de la semana local de un instante.</summary>
    public DayOfWeek DayOfWeekOf(DateTimeOffset instant) => ToLocalDate(instant).DayOfWeek;

    /// <summary>Lunes de la semana actual; si hoy es lunes, hoy.</summary>
    public DateOnly StartOfCurrentWeek(DateTimeOffset utcNow)
    {
        var today = Today(utcNow);
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        return today.AddDays(-daysSinceMonday);
    }

    /// <summary>Lunes de la semana siguiente.</summary>
    public DateOnly StartOfNextWeek(DateTimeOffset utcNow) => StartOfCurrentWeek(utcNow).AddDays(7);

    /// <summary>
    /// Convierte una fecha y hora locales a UTC.
    /// </summary>
    /// <returns><c>false</c> si esa hora local no existe porque el reloj se adelanta por horario de verano.</returns>
    public bool TryToUtc(DateOnly date, TimeOnly time, out DateTimeOffset utc)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (_zone.IsInvalidTime(local))
        {
            utc = default;
            return false;
        }

        utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _zone));
        return true;
    }

    /// <summary>Convierte una fecha y hora locales a UTC.</summary>
    /// <exception cref="ArgumentException">Si esa hora local no existe por el horario de verano.</exception>
    public DateTimeOffset ToUtc(DateOnly date, TimeOnly time) =>
        TryToUtc(date, time, out var utc)
            ? utc
            : throw new ArgumentException($"La hora {time} del {date} no existe en {TimeZoneId}.", nameof(time));
}
