using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.People.Patients;

namespace SGCM.Data.Application.People.Commands.CompletePatientProfile;

/// <summary>
/// Rechaza el perfil antes de llegar al handler si sus datos no son válidos. Las reglas viven
/// en <see cref="Patient.Create"/>; el validador solo las aplica para fallar pronto.
/// </summary>
public sealed class CompletePatientProfileCommandValidator : AbstractValidator<CompletePatientProfileCommand>
{
    // El usuario no viaja en el comando, lo resuelve el handler; aquí solo se validan los datos.
    private const int AnyValidUserId = 1;

    private static readonly Dictionary<string, string> PropertyByErrorCode = new()
    {
        [PatientErrors.FirstNameRequired.Code] = nameof(CompletePatientProfileCommand.FirstName),
        [PatientErrors.FirstNameTooLong.Code] = nameof(CompletePatientProfileCommand.FirstName),
        [PatientErrors.LastNameRequired.Code] = nameof(CompletePatientProfileCommand.LastName),
        [PatientErrors.LastNameTooLong.Code] = nameof(CompletePatientProfileCommand.LastName),
        [PatientErrors.InvalidBirthDate.Code] = nameof(CompletePatientProfileCommand.BirthDate),
        [PatientErrors.PhoneRequired.Code] = nameof(CompletePatientProfileCommand.PhoneNumber),
        [PatientErrors.PhoneTooLong.Code] = nameof(CompletePatientProfileCommand.PhoneNumber),
        [PatientErrors.AddressTooLong.Code] = nameof(CompletePatientProfileCommand.Address),
        [PatientErrors.IncompleteEmergencyContact.Code] = nameof(CompletePatientProfileCommand.EmergencyContactName),
        [PatientErrors.EmergencyContactNameTooLong.Code] = nameof(CompletePatientProfileCommand.EmergencyContactName),
        [PatientErrors.EmergencyContactPhoneTooLong.Code] = nameof(CompletePatientProfileCommand.EmergencyContactPhone),
        [PatientErrors.InvalidInsuranceProvider.Code] = nameof(CompletePatientProfileCommand.InsuranceProviderId),
        [PatientErrors.PolicyNumberRequired.Code] = nameof(CompletePatientProfileCommand.PolicyNumber),
        [PatientErrors.PolicyNumberTooLong.Code] = nameof(CompletePatientProfileCommand.PolicyNumber)
    };

    public CompletePatientProfileCommandValidator(IDateTimeProvider dateTimeProvider)
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            var result = Patient.Create(
                AnyValidUserId,
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

            if (result.IsSuccess)
            {
                return;
            }

            var error = result.Error;
            context.AddFailure(new ValidationFailure(PropertyByErrorCode[error.Code], error.Description)
            {
                ErrorCode = error.Code
            });
        });
    }
}
