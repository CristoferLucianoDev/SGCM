using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>
/// Consulta si los usuarios ya completaron el perfil de su rol.
/// </summary>
public interface IUserProfileReader
{
    /// <summary>
    /// Indica si el usuario ya completó el perfil de su rol. Lo usa el cálculo del paso
    /// pendiente, porque el perfil vive en otro agregado.
    /// </summary>
    /// <param name="userId">Id del usuario.</param>
    /// <param name="role">Rol del usuario, que decide en qué perfil buscar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns><c>true</c> si existe el perfil de ese rol para el usuario.</returns>
    Task<bool> HasProfileAsync(int userId, UserRole role, CancellationToken cancellationToken);
}
