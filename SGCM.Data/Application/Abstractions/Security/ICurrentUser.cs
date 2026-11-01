using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Abstractions.Security;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    bool IsInRole(UserRole role);

    /// <summary>
    /// Id del usuario autenticado (claim <see cref="AppClaims.UserId"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Si no hay un usuario autenticado. Solo se usa en endpoints con <c>[Authorize]</c>.
    /// </exception>
    int UserId { get; }
}
