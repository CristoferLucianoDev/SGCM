using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Identity.Commands.Logout;

/// <summary>
/// Cierra la sesión del refresh token recibido. Tiene éxito aunque el token no exista o la
/// sesión ya esté cerrada.
/// </summary>
internal sealed class LogoutCommandHandler(ISessionService sessionService) : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        await sessionService.EndAsync(command.RefreshToken, cancellationToken);

        return Unit.Value;
    }
}
