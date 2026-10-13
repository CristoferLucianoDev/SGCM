using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Abstractions.Security;

/// <summary>
/// Emite los access tokens de las sesiones de usuario.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Emite el access token JWT de una sesión. Lleva los claims de <see cref="AppClaims"/>,
    /// vence según <c>Jwt:AccessTokenMinutes</c> y va firmado con HMAC-SHA256.
    /// </summary>
    /// <param name="userId">Id del usuario dueño de la sesión.</param>
    /// <param name="role">Rol del usuario.</param>
    /// <param name="sessionId">
    /// Id de la sesión ya guardada; va en el claim <c>sid</c> para poder revocarla.
    /// </param>
    /// <returns>El token firmado y su vencimiento.</returns>
    AccessToken CreateAccessToken(int userId, UserRole role, int sessionId);
}
