using FluentValidation;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.ActivateSpecialty;

public sealed class ActivateSpecialtyCommandValidator : AbstractValidator<ActivateSpecialtyCommand>
{
    public ActivateSpecialtyCommandValidator()
    {
        RuleFor(command => command.Id)
            .GreaterThan(0)
            .WithErrorCode(SpecialtyErrors.InvalidId.Code)
            .WithMessage(SpecialtyErrors.InvalidId.Description);
    }
}
