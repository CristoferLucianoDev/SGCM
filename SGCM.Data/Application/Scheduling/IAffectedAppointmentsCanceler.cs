using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Cancela, como cancelación del médico y con un motivo común, las citas activas que quedan en los rangos que dejan
/// de estar disponibles. Lo llaman los comandos de cerrar y reprogramar un bloque y de editar la serie, antes de
/// cambiar el bloque.
/// </summary>
public interface IAffectedAppointmentsCanceler
{
    /// <summary>
    /// Cancela las citas afectadas y guarda las cancelaciones en la transacción en curso. Debe llamarse antes de
    /// cambiar el bloque: el trigger T4 rechaza cerrar o mover un bloque que todavía tiene citas activas, y en un
    /// mismo guardado no se puede asegurar que las citas se escriban antes que el bloque.
    /// </summary>
    /// <param name="doctorId">Médico dueño de los rangos.</param>
    /// <param name="removedRanges">Rangos que dejan de estar disponibles.</param>
    /// <param name="reason">Motivo del médico; obligatorio si hay citas afectadas.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Cuántas citas se cancelaron, o el error de la cancelación de una cita; ante un error no se guarda nada.</returns>
    Task<Result<int>> CancelAsync(
        int doctorId, IReadOnlyCollection<TimeRange> removedRanges, string? reason, CancellationToken cancellationToken);
}
