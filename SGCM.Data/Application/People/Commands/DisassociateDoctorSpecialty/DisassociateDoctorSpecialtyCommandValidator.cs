using FluentValidation;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.DisassociateDoctorSpecialty;

public sealed class DisassociateDoctorSpecialtyCommandValidator : AbstractValidator<DisassociateDoctorSpecialtyCommand>
{
    public DisassociateDoctorSpecialtyCommandValidator()
    {
        RuleFor(command => command.SpecialtyId).GreaterThan(0)
            .WithErrorCode(SpecialtyErrors.InvalidId.Code)
            .WithMessage(SpecialtyErrors.InvalidId.Description);
    }
}
