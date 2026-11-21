using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>
/// Lee las citas activas de varios médicos como intervalos de tiempo, sin cargar el agregado de la cita.
/// </summary>
public interface IActiveAppointmentReader
{
    /// <summary>
    /// Lista las citas activas (pendientes o confirmadas) de esos médicos que se cruzan con [from, to),
    /// agrupadas por médico y ordenadas por inicio. Los médicos sin citas no aparecen en el diccionario; sin ids
    /// devuelve un diccionario vacío sin consultar la base.
    /// </summary>
    /// <param name="doctorIds">Ids de los médicos.</param>
    /// <param name="from">Inicio de la ventana, incluido.</param>
    /// <param name="to">Fin de la ventana, excluido.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Los intervalos de cada médico, por id de médico.</returns>
    Task<IReadOnlyDictionary<int, IReadOnlyList<BookedInterval>>> ListActiveIntervalsAsync(
        IReadOnlyCollection<int> doctorIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
