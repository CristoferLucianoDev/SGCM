using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Abstractions.Security;

/// <summary>
/// Abre, renueva y cierra las sesiones de los usuarios, y les emite sus tokens.
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
    /// El usuario debe venir cargado con <see cref="IUserRepository.GetByEmailForUpdateAsync"/>
    /// o <see cref="IUserRepository.GetByIdForUpdateAsync"/>, dentro de la transacción del
    /// comando. Sin ese bloqueo, dos aperturas simultáneas del mismo usuario pueden revocar la
    /// misma sesión antigua y dejar más sesiones activas que el máximo. Aplica a todos los que
    /// abren sesión: el inicio de sesión, el segundo factor, el cambio de contraseña temporal
    /// y el completado del perfil.
    /// </remarks>
    Task<Result<SessionTokens>> StartAsync(
        User user,
        string? deviceName,
        string? ipAddress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renueva la sesión del refresh token presentado: lo reemplaza por uno nuevo y emite un
    /// access token con los pasos pendientes recalculados. Debe llamarse dentro de la transacción
    /// del comando, porque bloquea la fila del usuario para que dos renovaciones simultáneas del
    /// mismo token se ejecuten una tras otra.
    /// </summary>
    /// <param name="refreshToken">Refresh token en claro recibido del cliente.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// Los tokens nuevos; <see cref="UserErrors.SessionInvalid"/> si la sesión no se puede
    /// renovar; o <see cref="UserErrors.RefreshTokenReused"/> si se presentó un token ya rotado
    /// y se asume robo.
    /// </returns>
    /// <remarks>
    /// <see cref="UserErrors.RefreshTokenReused"/> deja revocadas las sesiones del usuario en el
    /// contexto: quien llama debe persistir ese cambio aunque el resultado sea un fallo.
    /// </remarks>
    Task<Result<SessionTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Cierra la sesión del refresh token presentado, sea el vigente o el anterior a la última
    /// rotación. Es idempotente: no hace nada si el token no existe o la sesión ya está revocada
    /// o vencida. Debe llamarse dentro de la transacción del comando, porque bloquea la fila del
    /// usuario.
    /// </summary>
    /// <param name="refreshToken">Refresh token en claro recibido del cliente.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando la sesión queda marcada como cerrada en el contexto.</returns>
    Task EndAsync(string refreshToken, CancellationToken cancellationToken);
}
