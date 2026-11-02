using FluentValidation;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.DeactivateSpecialty;

public sealed class DeactivateSpecialtyCommandValidator : AbstractValidator<DeactivateSpecialtyCommand>
{
    public DeactivateSpecialtyCommandValidator()
    {
        RuleFor(command => command.Id)
            .GreaterThan(0)
            .WithErrorCode(SpecialtyErrors.InvalidId.Code)
            .WithMessage(SpecialtyErrors.InvalidId.Description);
    }
}
