using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Patients;

namespace SGCM.Data.Application.People.Commands.UpdatePatientProfile;

/// <summary>
/// Actualiza el contacto y el seguro del perfil del paciente autenticado. No guarda: lo hace el
/// decorador de transacción, que además revierte todo si algún paso falla.
/// </summary>
internal sealed class UpdatePatientProfileCommandHandler(
    ICurrentUser currentUser,
    IPatientRepository patientRepository,
    IInsuranceProviderReader insuranceProviderReader) : ICommandHandler<UpdatePatientProfileCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdatePatientProfileCommand command, CancellationToken cancellationToken)
    {
        var patient = await patientRepository.GetByUserIdAsync(currentUser.UserId, cancellationToken);
        if (patient is null)
        {
            return PatientErrors.ProfileNotFound;
        }

        var contact = patient.UpdateContact(
            command.PhoneNumber, command.Address, command.EmergencyContactName, command.EmergencyContactPhone);
        if (contact.IsFailure)
        {
            return contact.Error;
        }

        if (command.InsuranceProviderId is not { } insuranceProviderId)
        {
            patient.RemoveInsurance();
            return Unit.Value;
        }

        // El proveedor solo se valida como activo cuando cambia: un paciente cuyo proveedor se
        // desactivó puede seguir actualizando sus demás datos.
        if (insuranceProviderId != patient.InsuranceProviderId
            && !await insuranceProviderReader.IsActiveAsync(insuranceProviderId, cancellationToken))
        {
            return PatientErrors.InvalidInsuranceProvider;
        }

        var insurance = patient.ChangeInsurance(insuranceProviderId, command.PolicyNumber);
        if (insurance.IsFailure)
        {
            return insurance.Error;
        }

        return Unit.Value;
    }
}
