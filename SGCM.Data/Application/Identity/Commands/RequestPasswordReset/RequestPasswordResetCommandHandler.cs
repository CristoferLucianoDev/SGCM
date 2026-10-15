using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.RequestPasswordReset;

/// <summary>
/// Emite el enlace para restablecer la contraseña si la cuenta existe, está confirmada y está
/// activa; en cualquier otro caso responde lo mismo sin emitir nada, para no revelar qué
/// cuentas existen. No guarda ni envía el correo: el decorador de transacción guarda, y el
/// evento UserTokenIssuedEvent dispara el envío después del commit.
/// </summary>
/// <remarks>
/// Limitación conocida: el correo se envía después del commit dentro de la misma petición,
/// así que una cuenta existente tarda más en responder y la latencia podría revelar si existe.
/// Se resolvería enviando el correo fuera de la petición, con un outbox o una cola.
/// </remarks>
internal sealed class RequestPasswordResetCommandHandler(
    IUserRepository userRepository,
    ISecureTokenGenerator tokenGenerator,
    IDateTimeProvider dateTimeProvider,
    IAttemptLimiter attemptLimiter,
    IOptions<AccountsOptions> accountsOptions,
    IOptions<PasswordResetLimitsOptions> limitsOptions) : ICommandHandler<RequestPasswordResetCommand, Unit>
{
    private const string AttemptKeyPrefix = "password-reset:";

    public async Task<Result<Unit>> HandleAsync(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        if (!TryRegisterAttempt(emailResult.Value))
        {
            return UserErrors.TooManyPasswordResetRequests;
        }

        // Bloquea la fila para que dos solicitudes simultáneas no dejen dos enlaces vigentes.
        var user = await userRepository.GetByEmailForUpdateAsync(emailResult.Value, cancellationToken);
        if (user is null || !user.IsEmailConfirmed || !user.IsActive)
        {
            return Unit.Value;
        }

        var token = tokenGenerator.Generate();
        user.IssueToken(
            UserTokenType.PasswordReset,
            token.Value,
            token.Hash,
            dateTimeProvider.UtcNow,
            TimeSpan.FromMinutes(accountsOptions.Value.PasswordResetTokenMinutes));

        return Unit.Value;
    }

    // Cuenta toda solicitud, exista o no el correo, para que el límite no revele qué cuentas
    // existen. No se reinicia: una solicitud atendida no prueba que el dueño la haya pedido.
    private bool TryRegisterAttempt(Email email)
    {
        var limits = limitsOptions.Value;

        return attemptLimiter.TryRegisterAttempt(
            AttemptKeyPrefix + email.Value,
            limits.MaxRequestsPerEmail,
            TimeSpan.FromMinutes(limits.EmailWindowMinutes));
    }
}
