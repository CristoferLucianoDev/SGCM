using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.ChangeTemporaryPassword;

/// <summary>Orquesta el cambio bajo bloqueo; el decorador guarda y confirma todos los cambios juntos.</summary>
internal sealed class ChangeTemporaryPasswordCommandHandler(
    ICurrentUser currentUser,
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IDateTimeProvider clock,
    ISessionService sessionService) : ICommandHandler<ChangeTemporaryPasswordCommand, SessionTokens>
{
    public async Task<Result<SessionTokens>> HandleAsync(
        ChangeTemporaryPasswordCommand command, CancellationToken cancellationToken)
    {
        var candidate = Password.Create(command.NewPassword);
        if (candidate.IsFailure) return candidate.Error;
        if (!currentUser.TryGetSessionIdentity(out var userId, out var sessionId))
            return UserErrors.SessionInvalid;

        // Primera carga del agregado en este scope: nunca verificar un hash anterior al bloqueo.
        var user = await userRepository.GetByIdForUpdateAsync(userId, cancellationToken);
        if (user is null) return UserErrors.SessionInvalid;

        var now = clock.UtcNow;
        var allowed = user.CanChangeTemporaryPassword(sessionId, now);
        if (allowed.IsFailure) return allowed.Error;
        if (passwordHasher.Verify(user.PasswordHash, candidate.Value.Value))
            return UserErrors.PasswordUnchanged;

        var changed = user.ChangeTemporaryPassword(passwordHasher.Hash(candidate.Value), sessionId, now);
        if (changed.IsFailure) return changed.Error;

        return await sessionService.RenewCurrentAsync(user, sessionId, now, cancellationToken);
    }
}

