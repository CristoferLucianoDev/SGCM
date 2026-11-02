using FluentValidation;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.AssociateDoctorSpecialty;

public sealed class AssociateDoctorSpecialtyCommandValidator : AbstractValidator<AssociateDoctorSpecialtyCommand>
{
    public AssociateDoctorSpecialtyCommandValidator()
    {
        RuleFor(command => command.SpecialtyId).GreaterThan(0)
            .WithErrorCode(SpecialtyErrors.InvalidId.Code)
            .WithMessage(SpecialtyErrors.InvalidId.Description);
    }
}
