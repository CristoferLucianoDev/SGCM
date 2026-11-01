using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;

namespace SGCM.Data.Application.People.Commands.CompletePatientProfile;

/// <summary>
/// Crea el perfil del paciente autenticado y le emite tokens nuevos. No confirma: lo hace el
/// decorador de transacción, así que si la sesión no se puede abrir el perfil tampoco queda.
/// </summary>
internal sealed class CompletePatientProfileCommandHandler(
    ICurrentUser currentUser,
    IUserRepository userRepository,
    IPatientRepository patientRepository,
    IInsuranceProviderReader insuranceProviderReader,
    ISessionService sessionService,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<CompletePatientProfileCommand, SessionTokens>
{
    public async Task<Result<SessionTokens>> HandleAsync(
        CompletePatientProfileCommand command,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;

        // Bloquea la fila: dos envíos simultáneos se atienden en orden y el segundo ve el perfil ya creado.
        var user = await userRepository.GetByIdForUpdateAsync(userId, cancellationToken);

        // Un token válido de una cuenta que ya no existe se trata como credencial inválida.
        if (user is null)
        {
            return UserErrors.InvalidCredentials;
        }

        if (await patientRepository.ExistsByUserIdAsync(userId, cancellationToken))
        {
            return PatientErrors.ProfileAlreadyExists;
        }

        var patientResult = Patient.Create(
            userId,
            command.FirstName,
            command.LastName,
            command.BirthDate,
            command.PhoneNumber,
            command.Address,
            command.EmergencyContactName,
            command.EmergencyContactPhone,
            command.InsuranceProviderId,
            command.PolicyNumber,
            dateTimeProvider.UtcNow);

        if (patientResult.IsFailure)
        {
            return patientResult.Error;
        }

        if (command.InsuranceProviderId is { } insuranceProviderId
            && !await insuranceProviderReader.IsActiveAsync(insuranceProviderId, cancellationToken))
        {
            return PatientErrors.InvalidInsuranceProvider;
        }

        patientRepository.Add(patientResult.Value);

        // StartAsync guarda el perfil junto con la sesión antes de calcular el paso pendiente,
        // así que los tokens nuevos ya salen sin él.
        return await sessionService.StartAsync(user, command.DeviceName, command.IpAddress, cancellationToken);
    }
}
