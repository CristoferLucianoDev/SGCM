using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.Common.ValueObjects;

namespace SGCM.Data.Application.Identity.Commands.CreateDoctorAccount;

/// <summary>
/// Rechaza el alta antes de llegar al handler si el correo no es válido, aplicando
/// la regla del value object <see cref="Email"/>.
/// </summary>
public sealed class CreateDoctorAccountCommandValidator : AbstractValidator<CreateDoctorAccountCommand>
{
    public CreateDoctorAccountCommandValidator()
    {
        RuleFor(command => command.Email).Custom((email, context) =>
        {
            var result = Email.Create(email);
            if (result.IsSuccess)
            {
                return;
            }

            context.AddFailure(new ValidationFailure(context.PropertyPath, result.Error.Description)
            {
                ErrorCode = result.Error.Code
            });
        });
    }
}