using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.ChangePassword;

/// <summary>
/// Orquesta el cambio bajo bloqueo; el decorador guarda y confirma la contraseña y las revocaciones juntas.
/// </summary>
internal sealed class ChangePasswordCommandHandler(
    ICurrentUser currentUser,
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IDateTimeProvider clock) : ICommandHandler<ChangePasswordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.CurrentPassword)) return UserErrors.CurrentPasswordRequired;
        var candidate = Password.Create(command.NewPassword);
        if (candidate.IsFailure) return candidate.Error;
        if (!currentUser.TryGetSessionIdentity(out var userId, out var sessionId))
            return UserErrors.SessionInvalid;

        // Primera carga del agregado en este scope: nunca verificar un hash anterior al bloqueo.
        var user = await userRepository.GetByIdForUpdateAsync(userId, cancellationToken);
        if (user is null) return UserErrors.SessionInvalid;

        var now = clock.UtcNow;
        var allowed = user.CanChangePassword(sessionId, now);
        if (allowed.IsFailure) return allowed.Error;
        if (!passwordHasher.Verify(user.PasswordHash, command.CurrentPassword))
            return UserErrors.CurrentPasswordIncorrect;

        // La actual ya se verificó, así que basta compararla en claro con la nueva.
        if (string.Equals(command.CurrentPassword, candidate.Value.Value, StringComparison.Ordinal))
            return UserErrors.NewPasswordSameAsCurrent;

        var changed = user.ChangePassword(passwordHasher.Hash(candidate.Value), sessionId, now);
        if (changed.IsFailure) return changed.Error;

        return Unit.Value;
    }
}
