using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Abstractions.Security;

/// <summary>
/// Abre las sesiones de los usuarios y les emite sus tokens.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Abre una sesión para el usuario ya autenticado y le emite su access token y su
    /// refresh token. Debe llamarse dentro de la transacción del comando, porque guarda
    /// la sesión para obtener su id antes de emitir el access token.
    /// </summary>
    /// <param name="user">Usuario ya autenticado.</param>
    /// <param name="deviceName">Nombre del dispositivo informado por el cliente, si lo hay.</param>
    /// <param name="ipAddress">Dirección IP del cliente, si se conoce.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// Los tokens de la sesión, o los errores de <see cref="User.StartSession"/>
    /// (<see cref="UserErrors.AccountInactive"/> o <see cref="UserErrors.EmailNotConfirmed"/>).
    /// </returns>
    /// <remarks>
    /// El usuario debe venir cargado con <see cref="IUserRepository.GetByEmailForUpdateAsync"/>,
    /// dentro de la transacción del comando. Sin ese bloqueo, dos aperturas simultáneas del
    /// mismo usuario pueden revocar la misma sesión antigua y dejar más sesiones activas que
    /// el máximo. Aplica a todos los que abren sesión: el inicio de sesión, el segundo factor
    /// y el cambio de contraseña temporal.
    /// </remarks>
    Task<Result<SessionTokens>> StartAsync(
        User user,
        string? deviceName,
        string? ipAddress,
        CancellationToken cancellationToken);
}
