using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Abstractions.Services;

/// <summary>
/// Calcula el hash de las contraseñas. La contraseña en claro nunca se persiste.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Devuelve el hash de la contraseña, con su sal incluida.
    /// </summary>
    string Hash(Password password);
}