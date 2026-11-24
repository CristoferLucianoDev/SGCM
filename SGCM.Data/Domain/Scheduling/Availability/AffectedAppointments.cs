using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Domain.Scheduling.Availability;

/// <summary>
/// Política de citas afectadas: las citas activas de un médico que quedan en los rangos que dejan de estar
/// disponibles, al cerrar o reprogramar un bloque o al editar la serie. Decide si el motivo es obligatorio y si debe
/// ser extendido con las mismas reglas que la cancelación de una cita por el médico (<see cref="StatusReason"/> y
/// <see cref="ShortNoticeCancellation"/>), así que el motivo que pasa aquí lo acepta cada cita al cancelarse.
/// </summary>
/// <param name="Appointments">Citas activas afectadas, ordenadas por inicio.</param>
/// <param name="IsShortNotice">Si alguna empieza antes del umbral de cancelación tardía, contado desde ahora.</param>
/// <param name="ShortNotice">Umbral y palabras mínimas de la cancelación tardía.</param>
public sealed record AffectedAppointments(
    IReadOnlyList<BookedInterval> Appointments,
    bool IsShortNotice,
    ShortNoticeCancellation ShortNotice)
{
    /// <summary>Si hay citas que cancelar, el médico debe escribir el motivo.</summary>
    public bool IsReasonRequired => Appointments.Count > 0;

    /// <summary>
    /// Busca, entre las citas activas del médico, las que se cruzan con algún rango que deja de estar disponible.
    /// </summary>
    /// <param name="activeAppointments">Citas activas del médico, en cualquier orden.</param>
    /// <param name="removedRanges">Rangos que dejan de estar disponibles.</param>
    /// <param name="now">Instante actual.</param>
    /// <param name="shortNotice">Regla de cancelación tardía de la configuración.</param>
    /// <exception cref="ArgumentNullException">Si algún argumento es nulo.</exception>
    public static AffectedAppointments Evaluate(
        IEnumerable<BookedInterval> activeAppointments,
        IReadOnlyCollection<TimeRange> removedRanges,
        DateTimeOffset now,
        ShortNoticeCancellation shortNotice)
    {
        ArgumentNullException.ThrowIfNull(activeAppointments);
        ArgumentNullException.ThrowIfNull(removedRanges);
        ArgumentNullException.ThrowIfNull(shortNotice);

        var affected = activeAppointments
            .Where(appointment => removedRanges.Any(range => range.Overlaps(appointment.Range)))
            .OrderBy(appointment => appointment.Range.Start)
            .ThenBy(appointment => appointment.AppointmentId)
            .ToList();
        var isShortNotice = affected.Any(appointment => shortNotice.AppliesTo(appointment.Range.Start, now));

        return new AffectedAppointments(affected, isShortNotice, shortNotice);
    }

    /// <summary>
    /// Comprueba el motivo del médico antes de confirmar: obligatorio y dentro de la longitud máxima si hay citas, y
    /// con las palabras mínimas si es extendido. Sin citas afectadas el motivo es opcional.
    /// </summary>
    /// <returns>
    /// <see cref="Error.None"/>, <see cref="AppointmentErrors.StatusReasonRequired"/>,
    /// <see cref="AppointmentErrors.StatusReasonTooLong"/> o <see cref="AppointmentErrors.ShortNoticeReasonTooShort"/>.
    /// </returns>
    public Error ValidateReason(string? reason)
    {
        if (!IsReasonRequired) return Error.None;

        var created = StatusReason.Create(reason);
        if (created.IsFailure) return created.Error;
        if (IsShortNotice && !created.Value.HasAtLeastWords(ShortNotice.MinimumWords))
            return AppointmentErrors.ShortNoticeReasonTooShort(ShortNotice.MinimumWords);

        return Error.None;
    }
}
