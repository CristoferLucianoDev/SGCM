using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.People.Patients;

/// <summary>
/// Contrato de persistencia del agregado <see cref="Patient"/>.
/// </summary>
public interface IPatientRepository : IRepository<Patient>
{
    /// <summary>
    /// Indica si el usuario ya tiene un perfil de paciente, activo o inactivo.
    /// </summary>
    /// <param name="userId">Id del usuario.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns><c>true</c> si el perfil existe.</returns>
    Task<bool> ExistsByUserIdAsync(int userId, CancellationToken cancellationToken = default);
}
