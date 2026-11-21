using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.People.Doctors;

namespace SGCM.Data.Application.People.Commands.UpdateDoctorProfile;

public sealed class UpdateDoctorProfileCommandValidator : AbstractValidator<UpdateDoctorProfileCommand>
{
    private static readonly Dictionary<string, string> PropertyByErrorCode = new()
    {
        [DoctorErrors.PhoneNumberRequired.Code] = nameof(UpdateDoctorProfileCommand.PhoneNumber),
        [DoctorErrors.PhoneNumberTooLong.Code] = nameof(UpdateDoctorProfileCommand.PhoneNumber),
        [DoctorErrors.InvalidProvinceId.Code] = nameof(UpdateDoctorProfileCommand.ProvinceId),
        [DoctorErrors.InvalidMunicipalityId.Code] = nameof(UpdateDoctorProfileCommand.MunicipalityId),
        [DoctorErrors.AddressLineRequired.Code] = nameof(UpdateDoctorProfileCommand.AddressLine),
        [DoctorErrors.AddressLineTooLong.Code] = nameof(UpdateDoctorProfileCommand.AddressLine),
        [DoctorErrors.InvalidConsultationFee.Code] = nameof(UpdateDoctorProfileCommand.ConsultationFee),
        [DoctorErrors.InvalidYearsOfExperience.Code] = nameof(UpdateDoctorProfileCommand.YearsOfExperience),
        [DoctorErrors.BiographyRequired.Code] = nameof(UpdateDoctorProfileCommand.Biography),
        [DoctorErrors.BiographyTooLong.Code] = nameof(UpdateDoctorProfileCommand.Biography)
    };

    public UpdateDoctorProfileCommandValidator()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            var result = Doctor.ValidateProfessionalData(command.PhoneNumber, command.ProvinceId, command.MunicipalityId,
                command.AddressLine, command.ConsultationFee, command.YearsOfExperience, command.Biography);
            if (result.IsFailure)
            {
                var error = result.Error;
                context.AddFailure(new ValidationFailure(PropertyByErrorCode[error.Code], error.Description)
                {
                    ErrorCode = error.Code
                });
            }
        });
    }
}
