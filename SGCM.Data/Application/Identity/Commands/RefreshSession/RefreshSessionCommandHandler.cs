using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.RefreshSession;

/// <summary>
/// Renueva la sesión del refresh token recibido.
/// </summary>
internal sealed class RefreshSessionCommandHandler(ISessionService sessionService)
    : ICommandHandler<RefreshSessionCommand, RefreshSessionResult>
{
    public async Task<Result<RefreshSessionResult>> HandleAsync(
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        var refreshed = await sessionService.RefreshAsync(command.RefreshToken, cancellationToken);

        if (refreshed.IsSuccess)
        {
            return RefreshSessionResult.Renewed(refreshed.Value);
        }

        // Se traduce a un éxito porque TransactionDecorator revierte todo resultado fallido, y
        // la revocación de las sesiones tiene que confirmarse.
        return refreshed.Error == UserErrors.RefreshTokenReused
            ? RefreshSessionResult.Revoked()
            : refreshed.Error;
    }
}
