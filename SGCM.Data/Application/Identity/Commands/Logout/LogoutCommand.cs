using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Identity.Commands.Logout;

/// <summary>
/// Cierre de la sesión a la que pertenece un refresh token.
/// </summary>
/// <param name="RefreshToken">Refresh token en claro.</param>
public sealed record LogoutCommand(string RefreshToken) : ICommand<Unit>
{
    /// <summary>
    /// Oculta el token, para que no se filtre si el comando termina en un log.
    /// </summary>
    public override string ToString() => $"{nameof(LogoutCommand)} {{ RefreshToken = ******** }}";
}
