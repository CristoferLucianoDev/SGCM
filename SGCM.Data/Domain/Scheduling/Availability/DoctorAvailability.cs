using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Availability.Events;
namespace SGCM.Data.Domain.Scheduling.Availability;

/// <summary>
/// Bloque de horario que el médico declara abierto. Puede venir de una serie o ser suelto.
/// El horario libre real es el bloque abierto menos las citas activas que lo traslapan.
/// </summary>
public sealed class DoctorAvailability : AggregateRoot
{
    public int DoctorId { get; private set; }

    /// <summary>Serie que generó el bloque; <c>null</c> si es un bloque suelto.</summary>
    public int? SeriesId { get; private set; }

    /// <summary>Inicio en UTC, en punto.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>Fin en UTC, en punto.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    /// <summary><c>false</c> si el médico cerró el bloque.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Ocurrencia editada a mano: editar la serie no la sobrescribe.</summary>
    public bool IsModified { get; private set; }

    private DoctorAvailability()
    {
    }

    /// <summary>
    /// Crea un bloque abierto y sin marca de modificado. Que no se traslape con otro bloque
    /// del mismo médico lo garantiza la base con su restricción de exclusión.
    /// </summary>
    /// <param name="seriesId">Serie que lo genera, o <c>null</c> para un bloque suelto.</param>
    /// <param name="minimumDurationHours">Duración mínima de un bloque, en horas, según la configuración.</param>
    /// <returns>El bloque con las fechas en UTC, o el primer error de validación encontrado.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración mínima no es positiva.</exception>
    public static Result<DoctorAvailability> Create(
        int doctorId,
        int? seriesId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        int minimumDurationHours)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumDurationHours);

        if (doctorId <= 0) return DoctorAvailabilityErrors.InvalidDoctor;
        if (seriesId <= 0) return DoctorAvailabilityErrors.InvalidSeries;
        var schedule = ValidateSchedule(startsAt, endsAt, minimumDurationHours);
        if (schedule != Error.None) return schedule;

        return new DoctorAvailability
        {
            DoctorId = doctorId,
            SeriesId = seriesId,
            StartsAt = startsAt.ToUniversalTime(),
            EndsAt = endsAt.ToUniversalTime(),
            IsAvailable = true,
            IsModified = false
        };
    }

    /// <summary>
    /// Crea un bloque suelto, sin serie, para un horario puntual futuro: abierto, sin marca de modificado,
    /// en hora en punto y con la duración mínima de la configuración.
    /// </summary>
    /// <param name="now">Instante actual; el bloque debe empezar después.</param>
    /// <param name="minimumDurationHours">Duración mínima de un bloque, en horas, según la configuración.</param>
    /// <returns>El bloque con las fechas en UTC, o el primer error de validación encontrado.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración mínima no es positiva.</exception>
    public static Result<DoctorAvailability> CreateLoose(
        int doctorId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        int minimumDurationHours,
        DateTimeOffset now)
    {
        var block = Create(doctorId, seriesId: null, startsAt, endsAt, minimumDurationHours);
        if (block.IsFailure) return block;

        return startsAt > now ? block : DoctorAvailabilityErrors.StartsInPast;
    }

    /// <summary>
    /// Cambia el horario del bloque con las reglas de cualquier bloque (hora en punto, fin después del inicio y la
    /// duración mínima de la configuración) y la regla de semanas, que se aplica al día actual y al nuevo. El bloque
    /// no debe haber empezado y el horario nuevo debe ser futuro. Que no se traslape con otro bloque del médico lo
    /// comprueba aplicación y lo garantiza la base.
    /// </summary>
    /// <remarks>
    /// Es el punto único de reprogramación, así que la edición de una ocurrencia también sigue la regla de semanas.
    /// Si el horario nuevo deja de cubrir parte del anterior, emite <see cref="DoctorAvailabilityRescheduledEvent"/>
    /// con esos rangos y el motivo, para cancelar las citas que quedan fuera.
    /// </remarks>
    /// <param name="now">Instante actual.</param>
    /// <param name="calendar">Calendario de la agenda, para la regla de semanas.</param>
    /// <param name="reason">Motivo del médico; se recorta y, si queda vacío, el evento lleva <c>null</c>.</param>
    /// <returns>El primer error de validación; ante un error el bloque no cambia ni emite nada.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración mínima no es positiva.</exception>
    /// <exception cref="ArgumentNullException">Si el calendario es nulo.</exception>
    public Result Reschedule(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        int minimumDurationHours,
        DateTimeOffset now,
        BusinessCalendar calendar,
        string? reason = null)
    {
        var valid = ValidateReschedule(startsAt, endsAt, minimumDurationHours, now, calendar);
        if (valid != Error.None) return valid;

        var previous = TimeRange.Create(StartsAt, EndsAt).Value;
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();

        var removed = previous.Subtract(TimeRange.Create(StartsAt, EndsAt).Value);
        if (removed.Count > 0)
        {
            var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            Raise(new DoctorAvailabilityRescheduledEvent(Id, DoctorId, removed, trimmed));
        }

        return Result.Success();
    }

    /// <summary>
    /// Comprueba si el bloque se puede reprogramar a ese horario, sin cambiarlo: abierto, sin haber empezado, con un
    /// horario nuevo válido y futuro, y con la regla de semanas para el día actual y el nuevo. Permite rechazar la
    /// reprogramación antes de cancelar las citas afectadas, con el mismo error que <see cref="Reschedule"/>.
    /// </summary>
    /// <returns><see cref="Error.None"/> o el primer error encontrado.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración mínima no es positiva.</exception>
    /// <exception cref="ArgumentNullException">Si el calendario es nulo.</exception>
    public Error ValidateReschedule(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        int minimumDurationHours,
        DateTimeOffset now,
        BusinessCalendar calendar)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumDurationHours);
        ArgumentNullException.ThrowIfNull(calendar);

        if (!IsAvailable) return DoctorAvailabilityErrors.AlreadyClosed;
        if (StartsAt <= now) return DoctorAvailabilityErrors.AlreadyStarted;
        var schedule = ValidateSchedule(startsAt, endsAt, minimumDurationHours);
        if (schedule != Error.None) return schedule;
        if (startsAt <= now) return DoctorAvailabilityErrors.StartsInPast;

        var currentWeek = RescheduleWeekRule.Check(calendar.ToLocalDate(StartsAt), now, calendar);
        if (currentWeek != Error.None) return currentWeek;

        return RescheduleWeekRule.Check(calendar.ToLocalDate(startsAt), now, calendar);
    }

    /// <summary>
    /// Edita "solo esta ocurrencia": reprograma el bloque de la serie dentro del mismo día local y lo marca como
    /// modificado, para que editar toda la serie no lo sobrescriba. Los demás días de la serie no cambian.
    /// </summary>
    /// <param name="calendar">Calendario de la agenda, para decidir el día local del bloque y la regla de semanas.</param>
    /// <param name="reason">Motivo del médico para las citas que queden fuera del horario nuevo.</param>
    /// <returns>El primer error de validación; ante un error el bloque no cambia ni se marca.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración mínima no es positiva.</exception>
    public Result RescheduleOccurrence(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        int minimumDurationHours,
        DateTimeOffset now,
        BusinessCalendar calendar,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (SeriesId is null) return DoctorAvailabilityErrors.NotAnOccurrence;
        // Si la ocurrencia pasara a otro día, la generación de bloques vería vacío el día original y lo
        // volvería a crear.
        if (calendar.ToLocalDate(startsAt) != calendar.ToLocalDate(StartsAt))
            return DoctorAvailabilityErrors.OccurrenceDayChanged;

        var rescheduled = Reschedule(startsAt, endsAt, minimumDurationHours, now, calendar, reason);
        if (rescheduled.IsFailure) return rescheduled;

        IsModified = true;
        return Result.Success();
    }

    /// <summary>
    /// Indica si el bloque todavía puede reprogramarse: está abierto, no ha empezado y la regla de semanas permite
    /// moverlo (no es de la semana actual, y si es de la siguiente, hoy es de lunes a miércoles).
    /// </summary>
    public bool CanReschedule(DateTimeOffset now, BusinessCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        return IsAvailable && StartsAt > now
            && RescheduleWeekRule.Check(calendar.ToLocalDate(StartsAt), now, calendar) == Error.None;
    }

    /// <summary>
    /// Cierra el bloque en cualquier semana, incluida la actual: deja de ofrecer horarios de inmediato. Emite
    /// <see cref="DoctorAvailabilityClosedEvent"/> con el rango del bloque y el motivo, para cancelar las citas
    /// activas que quedan dentro. Si el motivo es obligatorio lo decide la política de citas afectadas, no el bloque.
    /// </summary>
    /// <param name="reason">Motivo del médico; se recorta y, si queda vacío, el evento lleva <c>null</c>.</param>
    /// <param name="now">Instante actual.</param>
    /// <returns>El primer error de validación; ante un error el bloque no cambia ni emite nada.</returns>
    public Result Close(string? reason, DateTimeOffset now)
    {
        var closable = ValidateClose(now);
        if (closable != Error.None) return closable;

        IsAvailable = false;
        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Raise(new DoctorAvailabilityClosedEvent(Id, DoctorId, TimeRange.Create(StartsAt, EndsAt).Value, trimmed));
        return Result.Success();
    }

    /// <summary>
    /// Comprueba si el bloque se puede cerrar ahora, sin cerrarlo. Permite rechazar el cierre antes de cancelar las
    /// citas afectadas, con el mismo error que <see cref="Close"/>.
    /// </summary>
    /// <returns><see cref="Error.None"/>, <see cref="DoctorAvailabilityErrors.AlreadyClosed"/> o <see cref="DoctorAvailabilityErrors.AlreadyEnded"/>.</returns>
    public Error ValidateClose(DateTimeOffset now)
    {
        if (!IsAvailable) return DoctorAvailabilityErrors.AlreadyClosed;
        if (EndsAt <= now) return DoctorAvailabilityErrors.AlreadyEnded;

        return Error.None;
    }

    /// <summary>
    /// Crea el bloque de una ocurrencia de la serie, con el médico y la serie de esta.
    /// La serie debe estar guardada para tener su Id.
    /// </summary>
    public static Result<DoctorAvailability> CreateFromSeries(
        DoctorAvailabilitySeries series,
        AvailabilityOccurrence occurrence,
        int minimumDurationHours)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(occurrence);

        return Create(series.DoctorId, series.Id, occurrence.StartsAt, occurrence.EndsAt, minimumDurationHours);
    }

    // Reglas de horario comunes a crear y reprogramar.
    private static Error ValidateSchedule(DateTimeOffset startsAt, DateTimeOffset endsAt, int minimumDurationHours)
    {
        if (!HourAlignedInstant.IsOnHour(startsAt) || !HourAlignedInstant.IsOnHour(endsAt))
            return DoctorAvailabilityErrors.NotOnHour;
        if (!TimeRange.IsValid(startsAt, endsAt)) return DoctorAvailabilityErrors.InvalidTimeRange;
        if (endsAt - startsAt < TimeSpan.FromHours(minimumDurationHours)) return DoctorAvailabilityErrors.DurationTooShort;

        return Error.None;
    }
}
