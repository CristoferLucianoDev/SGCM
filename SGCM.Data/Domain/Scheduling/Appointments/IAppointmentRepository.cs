using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Appointments;

public interface IAppointmentRepository : IRepository<Appointment>
{
    /// <summary>Busca una cita por su id, rastreada para poder cambiarla.</summary>
    /// <returns>La cita, o <c>null</c> si no existe.</returns>
    Task<Appointment?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Bloquea la cita con SELECT ... FOR UPDATE y la carga rastreada. Requiere una transacción activa.
    /// Si la cita ya estaba rastreada sin cambios, se descarta esa copia y se vuelve a leer después del
    /// bloqueo: así, si otra transacción la cambió mientras esta esperaba, se lee su estado nuevo.
    /// </summary>
    /// <returns>La cita, o <c>null</c> si no existe.</returns>
    /// <exception cref="InvalidOperationException">
    /// Si no hay una transacción activa o la cita ya rastreada tiene cambios pendientes.
    /// </exception>
    Task<Appointment?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken);
}
