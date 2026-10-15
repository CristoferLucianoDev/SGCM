using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.ResetPassword;

/// <summary>
/// Busca al usuario por el hash del token, con su fila bloqueada para que dos usos simultáneos
/// del mismo enlace no lo consuman los dos, y le restablece la contraseña. El vencimiento, el
/// uso y el reemplazo del token los decide el dominio. No guarda: el decorador de transacción
/// lo hace.
/// </summary>
internal sealed class ResetPasswordCommandHandler(
    IUserRepository userRepository,
    ISecureTokenGenerator tokenGenerator,
    IPasswordHasher passwordHasher,
    IDateTimeProvider dateTimeProvider,
    IAttemptLimiter attemptLimiter) : ICommandHandler<ResetPasswordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        // No basta con el validador: Hash lanza una excepción si recibe null.
        if (string.IsNullOrWhiteSpace(command.Token))
        {
            return UserErrors.TokenInvalid;
        }

        var passwordResult = Password.Create(command.NewPassword);
        if (passwordResult.IsFailure)
        {
            return passwordResult.Error;
        }

        var tokenHash = tokenGenerator.Hash(command.Token);

        var user = await userRepository.GetByTokenHashForUpdateAsync(UserTokenType.PasswordReset, tokenHash, cancellationToken);
        if (user is null)
        {
            return UserErrors.TokenInvalid;
        }

        var now = dateTimeProvider.UtcNow;

        // Descarta un enlace inválido antes de calcular el hash, que es caro y se haría con la fila bloqueada.
        var allowed = user.CanResetPassword(tokenHash, now);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var result = user.ResetPassword(tokenHash, passwordHasher.Hash(passwordResult.Value), now);
        if (result.IsFailure)
        {
            return result.Error;
        }

        // Quien pidió la recuperación pudo haberse bloqueado al olvidar la contraseña; con la
        // nueva ya demostró ser el dueño del correo y debe poder iniciar sesión de inmediato.
        attemptLimiter.Reset(AttemptKeys.Login(user.Email));

        return Unit.Value;
    }
}
