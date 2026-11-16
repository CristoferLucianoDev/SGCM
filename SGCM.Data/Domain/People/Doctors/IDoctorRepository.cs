using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.People.Doctors;

public interface IDoctorRepository : IRepository<Doctor>
{
    /// <summary>Comprueba la existencia de una licencia validada y normalizada por el dominio, incluso en perfiles inactivos.</summary>
    Task<bool> ExistsByLicenseNumberAsync(string licenseNumber, CancellationToken cancellationToken);

    /// <summary>Carga el perfil completo del usuario bajo un bloqueo que exige una transacción activa.</summary>
    Task<Doctor?> GetByUserIdForUpdateAsync(int userId, CancellationToken cancellationToken);
}
