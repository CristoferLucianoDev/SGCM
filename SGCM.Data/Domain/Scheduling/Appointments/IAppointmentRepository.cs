using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Appointments;

public interface IAppointmentRepository : IRepository<Appointment>
{
    /// <summary>Busca una cita por su id, rastreada para poder cambiarla.</summary>
    /// <returns>La cita, o <c>null</c> si no existe.</returns>
    Task<Appointment?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
