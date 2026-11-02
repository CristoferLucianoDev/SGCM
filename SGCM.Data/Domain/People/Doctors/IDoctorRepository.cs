using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.People.Doctors;

public interface IDoctorRepository : IRepository<Doctor>
{
    /// <summary>Carga el perfil completo del usuario bajo un bloqueo que exige una transacción activa.</summary>
    Task<Doctor?> GetByUserIdForUpdateAsync(int userId, CancellationToken cancellationToken);
}
