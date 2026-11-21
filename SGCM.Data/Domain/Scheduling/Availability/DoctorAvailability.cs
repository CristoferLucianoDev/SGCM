using SGCM.Data.Domain.Common;
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
    /// Cambia el horario del bloque con las reglas de cualquier bloque: hora en punto, fin después del inicio
    /// y la duración mínima de la configuración. El bloque no debe haber empezado y el horario nuevo debe
    /// ser futuro. Que no se traslape con otro bloque del médico lo comprueba aplicación y lo garantiza la base.
    /// </summary>
    /// <remarks>
    /// Es el punto único de reprogramación: las reglas por semana y el aviso a las citas afectadas del PBI
    /// Reprogramar bloque o serie se agregan aquí, y así también aplican a la edición de una ocurrencia.
    /// </remarks>
    /// <param name="now">Instante actual.</param>
    /// <returns>El primer error de validación; ante un error el bloque no cambia.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración mínima no es positiva.</exception>
    public Result Reschedule(DateTimeOffset startsAt, DateTimeOffset endsAt, int minimumDurationHours, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumDurationHours);

        if (StartsAt <= now) return DoctorAvailabilityErrors.AlreadyStarted;
        var schedule = ValidateSchedule(startsAt, endsAt, minimumDurationHours);
        if (schedule != Error.None) return schedule;
        if (startsAt <= now) return DoctorAvailabilityErrors.StartsInPast;

        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        return Result.Success();
    }

    /// <summary>
    /// Edita "solo esta ocurrencia": reprograma el bloque de la serie dentro del mismo día local y lo marca como
    /// modificado, para que editar toda la serie no lo sobrescriba. Los demás días de la serie no cambian.
    /// </summary>
    /// <param name="calendar">Calendario de la agenda, para decidir el día local del bloque.</param>
    /// <returns>El primer error de validación; ante un error el bloque no cambia ni se marca.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración mínima no es positiva.</exception>
    public Result RescheduleOccurrence(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        int minimumDurationHours,
        DateTimeOffset now,
        BusinessCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (SeriesId is null) return DoctorAvailabilityErrors.NotAnOccurrence;
        // Si la ocurrencia pasara a otro día, la generación de bloques vería vacío el día original y lo
        // volvería a crear.
        if (calendar.ToLocalDate(startsAt) != calendar.ToLocalDate(StartsAt))
            return DoctorAvailabilityErrors.OccurrenceDayChanged;

        var rescheduled = Reschedule(startsAt, endsAt, minimumDurationHours, now);
        if (rescheduled.IsFailure) return rescheduled;

        IsModified = true;
        return Result.Success();
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
