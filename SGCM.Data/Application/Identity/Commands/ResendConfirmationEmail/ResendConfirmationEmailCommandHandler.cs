using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.ResendConfirmationEmail;

/// <summary>
/// Emite un enlace de confirmación nuevo para un registro pendiente. Carga al usuario con
/// la fila bloqueada, para que la limpieza no lo borre mientras recibe un enlace nuevo y
/// para que dos reenvíos simultáneos no emitan dos enlaces. No guarda ni envía el correo:
/// el decorador de transacción guarda, y el evento UserTokenIssuedEvent dispara el envío
/// después del commit.
/// </summary>
internal sealed class ResendConfirmationEmailCommandHandler(
    IUserRepository userRepository,
    ISecureTokenGenerator tokenGenerator,
    IDateTimeProvider dateTimeProvider,
    IOptions<AccountsOptions> accountsOptions) : ICommandHandler<ResendConfirmationEmailCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ResendConfirmationEmailCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var user = await userRepository.GetByEmailForUpdateAsync(emailResult.Value, cancellationToken);
        if (user is null)
        {
            // El PBI trata un registro vencido como inexistente, y la limpieza pudo haberlo borrado.
            return UserErrors.RegistrationExpired;
        }

        var token = tokenGenerator.Generate();
        var options = accountsOptions.Value;
        var result = user.ResendEmailConfirmation(
            token.Value,
            token.Hash,
            dateTimeProvider.UtcNow,
            TimeSpan.FromMinutes(options.EmailConfirmationTokenMinutes),
            TimeSpan.FromSeconds(options.ResendConfirmationCooldownSeconds));

        return result.IsFailure ? Result<Unit>.Failure(result.Error) : Unit.Value;
    }
}
