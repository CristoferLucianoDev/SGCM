using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>
/// Lee las citas activas de un médico como intervalos de tiempo, sin cargar el agregado de la cita.
/// </summary>
public interface IActiveAppointmentReader
{
    /// <summary>
    /// Lista las citas activas (pendientes o confirmadas) del médico que se cruzan con [from, to).
    /// </summary>
    /// <param name="doctorId">Id del médico.</param>
    /// <param name="from">Inicio de la ventana, incluido.</param>
    /// <param name="to">Fin de la ventana, excluido.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Los intervalos ordenados por inicio; lista vacía si no hay citas.</returns>
    Task<IReadOnlyList<BookedInterval>> ListActiveIntervalsAsync(
        int doctorId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
