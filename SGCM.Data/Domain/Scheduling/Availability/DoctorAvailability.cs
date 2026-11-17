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
        if (!HourAlignedInstant.IsOnHour(startsAt) || !HourAlignedInstant.IsOnHour(endsAt))
            return DoctorAvailabilityErrors.NotOnHour;
        if (!TimeRange.IsValid(startsAt, endsAt)) return DoctorAvailabilityErrors.InvalidTimeRange;
        if (endsAt - startsAt < TimeSpan.FromHours(minimumDurationHours))
            return DoctorAvailabilityErrors.DurationTooShort;

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
}
