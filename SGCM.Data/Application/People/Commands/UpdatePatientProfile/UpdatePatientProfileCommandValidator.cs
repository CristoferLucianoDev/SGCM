using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.People.Patients;

namespace SGCM.Data.Application.People.Commands.UpdatePatientProfile;

/// <summary>
/// Rechaza los datos antes de llegar al handler si no son válidos. Las reglas viven en
/// <see cref="Patient.Create"/>; el validador solo las aplica para fallar pronto.
/// </summary>
public sealed class UpdatePatientProfileCommandValidator : AbstractValidator<UpdatePatientProfileCommand>
{
    // El comando no trae identidad, que no cambia; estos valores válidos solo completan Create
    // para validar los datos editables.
    private const int AnyValidUserId = 1;
    private const string AnyValidName = "Paciente";
    private static readonly DateOnly AnyValidBirthDate = new(2000, 1, 1);

    private static readonly Dictionary<string, string> PropertyByErrorCode = new()
    {
        [PatientErrors.PhoneRequired.Code] = nameof(UpdatePatientProfileCommand.PhoneNumber),
        [PatientErrors.PhoneTooLong.Code] = nameof(UpdatePatientProfileCommand.PhoneNumber),
        [PatientErrors.AddressTooLong.Code] = nameof(UpdatePatientProfileCommand.Address),
        [PatientErrors.IncompleteEmergencyContact.Code] = nameof(UpdatePatientProfileCommand.EmergencyContactName),
        [PatientErrors.EmergencyContactNameTooLong.Code] = nameof(UpdatePatientProfileCommand.EmergencyContactName),
        [PatientErrors.EmergencyContactPhoneTooLong.Code] = nameof(UpdatePatientProfileCommand.EmergencyContactPhone),
        [PatientErrors.InvalidInsuranceProvider.Code] = nameof(UpdatePatientProfileCommand.InsuranceProviderId),
        [PatientErrors.PolicyNumberRequired.Code] = nameof(UpdatePatientProfileCommand.PolicyNumber),
        [PatientErrors.PolicyNumberTooLong.Code] = nameof(UpdatePatientProfileCommand.PolicyNumber)
    };

    public UpdatePatientProfileCommandValidator(IDateTimeProvider dateTimeProvider)
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            var result = Patient.Create(
                AnyValidUserId,
                AnyValidName,
                AnyValidName,
                AnyValidBirthDate,
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
