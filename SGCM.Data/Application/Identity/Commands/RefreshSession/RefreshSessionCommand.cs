using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Identity.Commands.RefreshSession;

/// <summary>
/// Renovación de una sesión con su refresh token.
/// </summary>
/// <param name="RefreshToken">Refresh token en claro.</param>
public sealed record RefreshSessionCommand(string RefreshToken) : ICommand<RefreshSessionResult>
{
    /// <summary>
    /// Oculta el token, para que no se filtre si el comando termina en un log.
    /// </summary>
    public override string ToString() => $"{nameof(RefreshSessionCommand)} {{ RefreshToken = ******** }}";
}
