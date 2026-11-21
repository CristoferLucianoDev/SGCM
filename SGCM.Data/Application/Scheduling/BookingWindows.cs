using SGCM.Data.Domain.Scheduling;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Único lugar donde se calcula la ventana de reservas, para que la consulta de horarios libres y la reserva de
/// cita no puedan discrepar. La ventana limita cuándo puede EMPEZAR una cita.
/// </summary>
internal static class BookingWindows
{
    private const int HoursPerDay = 24;

    /// <summary>
    /// Ventana del paciente: desde las 00:00 locales del día de hoy más la antelación mínima hasta las 00:00
    /// locales del día siguiente al horizonte, de modo que el día del horizonte entra completo, igual que los
    /// bloques que genera <see cref="AvailabilityBlockGenerator"/>. La ventana nunca empieza antes de ahora: si
    /// esas 00:00 ya pasaron, como ocurre con antelación 0, el inicio es el instante actual.
    /// </summary>
    /// <param name="utcNow">Instante actual.</param>
    /// <param name="calendar">Calendario de la agenda; "hoy" es su fecha local, no la de UTC.</param>
    /// <param name="options">Configuración de la agenda ya validada.</param>
    /// <returns>El rango [inicio, fin) en UTC.</returns>
    public static TimeRange ForPatient(DateTimeOffset utcNow, BusinessCalendar calendar, SchedulingOptions options)
    {
        var today = calendar.Today(utcNow);
        var firstInstant = FirstInstantOfLocalDay(calendar, today.AddDays(options.MinimumBookingLeadDays));
        // Con antelación 0 la medianoche de hoy ya pasó; sin este tope se ofrecerían horarios vencidos.
        var start = firstInstant >= utcNow ? firstInstant : utcNow.ToUniversalTime();
        return RangeFromValidatedConfiguration(start, EndOfWindow(utcNow, calendar, options));
    }

    /// <summary>
    /// Ventana del médico sobre sus propios horarios: desde el instante actual y hasta el mismo fin que la del
    /// paciente.
    /// </summary>
    /// <param name="utcNow">Instante actual.</param>
    /// <param name="calendar">Calendario de la agenda.</param>
    /// <param name="options">Configuración de la agenda ya validada.</param>
    /// <returns>El rango [inicio, fin) en UTC.</returns>
    public static TimeRange ForDoctor(DateTimeOffset utcNow, BusinessCalendar calendar, SchedulingOptions options) =>
        RangeFromValidatedConfiguration(utcNow, EndOfWindow(utcNow, calendar, options));

    private static DateTimeOffset EndOfWindow(DateTimeOffset utcNow, BusinessCalendar calendar, SchedulingOptions options)
    {
        var horizonDay = calendar.Today(utcNow).AddDays(options.BookingHorizonDays);
        return FirstInstantOfLocalDay(calendar, horizonDay.AddDays(1));
    }

    /// <summary>
    /// UTC de las 00:00 locales; si esa hora no existe porque el reloj se adelanta a medianoche, la primera hora
    /// local que sí existe ese día.
    /// </summary>
    private static DateTimeOffset FirstInstantOfLocalDay(BusinessCalendar calendar, DateOnly date)
    {
        for (var hour = 0; hour < HoursPerDay; hour++)
        {
            if (calendar.TryToUtc(date, new TimeOnly(hour, 0), out var utc)) return utc;
        }

        throw new InvalidOperationException($"Ninguna hora del {date} existe en {calendar.TimeZoneId}.");
    }

    // Con MinimumBookingLeadDays < BookingHorizonDays el fin siempre es posterior al inicio.
    private static TimeRange RangeFromValidatedConfiguration(DateTimeOffset start, DateTimeOffset end) =>
        TimeRange.Create(start, end).Value;
}
