namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>
/// Consulta el estado del segundo factor de los usuarios.
/// </summary>
public interface ITwoFactorStatusReader
{
    /// <summary>
    /// Indica si el usuario tiene el segundo factor activo.
    /// </summary>
    /// <param name="userId">Id del usuario.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns><c>true</c> si el usuario tiene el segundo factor activo.</returns>
    Task<bool> IsEnabledAsync(int userId, CancellationToken cancellationToken);
}
