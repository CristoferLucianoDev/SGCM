using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.CreateSpecialty;

public sealed class CreateSpecialtyCommandValidator : AbstractValidator<CreateSpecialtyCommand>
{
    public CreateSpecialtyCommandValidator()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            var result = Specialty.Create(command.Name, command.Description);
            if (result.IsSuccess)
            {
                return;
            }

            var error = result.Error;
            var property = error == SpecialtyErrors.NameRequired || error == SpecialtyErrors.NameTooLong
                ? nameof(command.Name)
                : nameof(command.Description);

            context.AddFailure(new ValidationFailure(property, error.Description)
            {
                ErrorCode = error.Code
            });
        });
    }
}
