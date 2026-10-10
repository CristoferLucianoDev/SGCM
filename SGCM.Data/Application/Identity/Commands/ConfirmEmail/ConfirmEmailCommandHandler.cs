using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.ConfirmEmail;

/// <summary>
/// Busca al usuario por el hash del token y confirma su correo. El vencimiento del
/// registro pendiente lo decide el dominio, que responde con RegistrationExpired.
/// No guarda: el decorador de transacción lo hace.
/// </summary>
internal sealed class ConfirmEmailCommandHandler(
    IUserRepository userRepository,
    ISecureTokenGenerator tokenGenerator,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<ConfirmEmailCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        // No basta con el validador: Hash lanza una excepción si recibe null.
        if (string.IsNullOrWhiteSpace(command.Token))
        {
            return UserErrors.TokenInvalid;
        }

        var tokenHash = tokenGenerator.Hash(command.Token);

        var user = await userRepository.GetByTokenHashAsync(UserTokenType.EmailConfirmation, tokenHash, cancellationToken);
        if (user is null)
        {
            return UserErrors.TokenInvalid;
        }

        var result = user.ConfirmEmail(tokenHash, dateTimeProvider.UtcNow);

        return result.IsFailure ? Result<Unit>.Failure(result.Error) : Unit.Value;
    }
}
