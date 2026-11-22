using FluentValidation;

namespace SGCM.Data.Application.People.Queries.GetDoctorProfile;

public sealed class GetDoctorProfileQueryValidator : AbstractValidator<GetDoctorProfileQuery>
{
    public GetDoctorProfileQueryValidator()
    {
        RuleFor(query => query.DoctorId)
            .GreaterThan(0)
            .WithErrorCode("Doctor.InvalidDoctorId")
            .WithMessage("El identificador del médico debe ser mayor que cero.");
    }
}
