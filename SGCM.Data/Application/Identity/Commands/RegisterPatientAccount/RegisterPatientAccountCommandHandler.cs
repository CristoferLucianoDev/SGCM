using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.RegisterPatientAccount;

/// <summary>
/// Crea la cuenta pendiente del paciente, o reemplaza un registro que nunca se confirmó.
/// Carga al usuario existente con la fila bloqueada, para que dos registros simultáneos del
/// mismo correo pendiente no emitan dos enlaces, que el índice único de tokens rechazaría, y
/// para que la limpieza no borre la fila mientras se reemplaza. No guarda ni envía el correo:
/// el decorador de transacción guarda, y el evento UserTokenIssuedEvent dispara el envío
/// después del commit.
/// </summary>
internal sealed class RegisterPatientAccountCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ISecureTokenGenerator tokenGenerator,
    IDateTimeProvider dateTimeProvider,
    IOptions<AccountsOptions> accountsOptions) : ICommandHandler<RegisterPatientAccountCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RegisterPatientAccountCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var passwordResult = Password.Create(command.Password);
        if (passwordResult.IsFailure)
        {
            return passwordResult.Error;
        }

        var existingUser = await userRepository.GetByEmailForUpdateAsync(emailResult.Value, cancellationToken);
        if (existingUser is { IsEmailConfirmed: true })
        {
            return UserErrors.EmailAlreadyInUse;
        }

        var registration = CreateRegistrationData(passwordResult.Value);

        return existingUser is null
            ? RegisterNew(emailResult.Value, registration)
            : ReplacePending(existingUser, registration);
    }

    private Result<Unit> RegisterNew(Email email, RegistrationData data)
    {
        var user = User.RegisterPending(email, data.PasswordHash, data.Token.Value, data.Token.Hash, data.Now, data.Lifetime);
        userRepository.Add(user);

        return Unit.Value;
    }

    private static Result<Unit> ReplacePending(User user, RegistrationData data)
    {
        var result = user.ReplacePendingRegistration(data.PasswordHash, data.Token.Value, data.Token.Hash, data.Now, data.Lifetime);

        return result.IsFailure ? Result<Unit>.Failure(result.Error) : Unit.Value;
    }

    private RegistrationData CreateRegistrationData(Password password) => new(
        passwordHasher.Hash(password),
        tokenGenerator.Generate(),
        dateTimeProvider.UtcNow,
        TimeSpan.FromMinutes(accountsOptions.Value.EmailConfirmationTokenMinutes));

    private sealed record RegistrationData(string PasswordHash, SecureToken Token, DateTimeOffset Now, TimeSpan Lifetime);
}