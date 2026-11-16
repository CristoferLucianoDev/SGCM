using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.CompleteDoctorProfile;

/// <summary>Aplica las reglas de <see cref="Doctor.Create"/> antes de abrir la transacción del alta.</summary>
public sealed class CompleteDoctorProfileCommandValidator : AbstractValidator<CompleteDoctorProfileCommand>
{
    // El identificador real se obtiene en el handler. Aquí se valida la entrada del perfil.
    private const int AnyValidUserId = 1;

    private static readonly Dictionary<string, string> PropertyByErrorCode = new()
    {
        [DoctorErrors.FirstNameRequired.Code] = nameof(CompleteDoctorProfileCommand.FirstName),
        [DoctorErrors.FirstNameTooLong.Code] = nameof(CompleteDoctorProfileCommand.FirstName),
        [DoctorErrors.LastNameRequired.Code] = nameof(CompleteDoctorProfileCommand.LastName),
        [DoctorErrors.LastNameTooLong.Code] = nameof(CompleteDoctorProfileCommand.LastName),
        [DoctorErrors.LicenseNumberRequired.Code] = nameof(CompleteDoctorProfileCommand.LicenseNumber),
        [DoctorErrors.LicenseNumberTooLong.Code] = nameof(CompleteDoctorProfileCommand.LicenseNumber),
        [DoctorErrors.PhoneNumberRequired.Code] = nameof(CompleteDoctorProfileCommand.PhoneNumber),
        [DoctorErrors.PhoneNumberTooLong.Code] = nameof(CompleteDoctorProfileCommand.PhoneNumber),
        [DoctorErrors.InvalidProvinceId.Code] = nameof(CompleteDoctorProfileCommand.ProvinceId),
        [DoctorErrors.InvalidMunicipalityId.Code] = nameof(CompleteDoctorProfileCommand.MunicipalityId),
        [DoctorErrors.AddressLineRequired.Code] = nameof(CompleteDoctorProfileCommand.AddressLine),
        [DoctorErrors.AddressLineTooLong.Code] = nameof(CompleteDoctorProfileCommand.AddressLine),
        [DoctorErrors.InvalidConsultationFee.Code] = nameof(CompleteDoctorProfileCommand.ConsultationFee),
        [DoctorErrors.InvalidYearsOfExperience.Code] = nameof(CompleteDoctorProfileCommand.YearsOfExperience),
        [DoctorErrors.BiographyRequired.Code] = nameof(CompleteDoctorProfileCommand.Biography),
        [DoctorErrors.BiographyTooLong.Code] = nameof(CompleteDoctorProfileCommand.Biography),
        [DoctorErrors.SpecialtyRequired.Code] = nameof(CompleteDoctorProfileCommand.SpecialtyIds),
        [DoctorErrors.SpecialtyAlreadyAssociated.Code] = nameof(CompleteDoctorProfileCommand.SpecialtyIds),
        [SpecialtyErrors.InvalidId.Code] = nameof(CompleteDoctorProfileCommand.SpecialtyIds)
    };

    public CompleteDoctorProfileCommandValidator()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            var result = command.CreateDoctor(AnyValidUserId);
            if (result.IsSuccess) return;

            var error = result.Error;
            context.AddFailure(new ValidationFailure(PropertyByErrorCode[error.Code], error.Description)
            {
                ErrorCode = error.Code
            });
        });
    }
}
