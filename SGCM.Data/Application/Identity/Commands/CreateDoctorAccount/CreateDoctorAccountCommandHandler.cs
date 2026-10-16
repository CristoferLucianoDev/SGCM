using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.CreateDoctorAccount;

/// <summary>
/// Crea la cuenta de un médico con una contraseña temporal aleatoria. Guarda antes de
/// terminar para devolver el id; el decorador de transacción hace el commit.
/// </summary>
internal sealed class CreateDoctorAccountCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ITemporaryPasswordGenerator passwordGenerator,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateDoctorAccountCommand, CreateDoctorAccountResult>
{
    public async Task<Result<CreateDoctorAccountResult>> HandleAsync(
        CreateDoctorAccountCommand command,
        CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        // Cualquier cuenta con ese correo, pendiente, confirmada o inactiva, bloquea el alta.
        var existing = await userRepository.GetByEmailAsync(emailResult.Value, cancellationToken);
        if (existing is not null)
        {
            return UserErrors.EmailAlreadyInUse;
        }

        var temporaryPassword = passwordGenerator.Generate();

        // El generador siempre cumple la política; si no, es un error de programación.
        var password = Password.Create(temporaryPassword);
        if (password.IsFailure)
        {
            throw new InvalidOperationException("La contraseña temporal generada no cumple la política de contraseñas.");
        }

        var user = User.CreateDoctor(emailResult.Value, passwordHasher.Hash(password.Value));
        userRepository.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateDoctorAccountResult(user.Id, user.Email.Value, temporaryPassword);
    }
}