using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Aplica la política de citas afectadas sobre las citas activas guardadas. La usan cerrar y reprogramar un bloque
/// y editar la serie, y la consulta que avisa al médico antes de confirmar.
/// </summary>
public interface IAffectedAppointmentsPolicy
{
    /// <summary>Evalúa las citas activas del médico que quedan en los rangos que dejan de estar disponibles.</summary>
    /// <param name="doctorId">Médico dueño de los rangos.</param>
    /// <param name="removedRanges">Rangos que dejan de estar disponibles; sin rangos no hay citas afectadas.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<AffectedAppointments> EvaluateAsync(
        int doctorId, IReadOnlyCollection<TimeRange> removedRanges, CancellationToken cancellationToken);
}
