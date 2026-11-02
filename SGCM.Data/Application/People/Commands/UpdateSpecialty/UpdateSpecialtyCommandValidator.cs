using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.UpdateSpecialty;

public sealed class UpdateSpecialtyCommandValidator : AbstractValidator<UpdateSpecialtyCommand>
{
    public UpdateSpecialtyCommandValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.Id)
            .GreaterThan(0)
            .WithErrorCode(SpecialtyErrors.InvalidId.Code)
            .WithMessage(SpecialtyErrors.InvalidId.Description);

        RuleFor(command => command).Custom((command, context) =>
        {
            var candidate = Specialty.Create("Especialidad", "Descripción.").Value;
            var result = candidate.Update(
                command.NameProvided, command.Name, command.DescriptionProvided, command.Description);
            if (result.IsSuccess)
            {
                return;
            }

            var error = result.Error;
            var property = error == SpecialtyErrors.NoFieldsProvided
                ? string.Empty
                : error == SpecialtyErrors.NameRequired || error == SpecialtyErrors.NameTooLong
                    ? nameof(command.Name)
                    : nameof(command.Description);

            context.AddFailure(new ValidationFailure(property, error.Description)
            {
                ErrorCode = error.Code
            });
        });
    }
}
