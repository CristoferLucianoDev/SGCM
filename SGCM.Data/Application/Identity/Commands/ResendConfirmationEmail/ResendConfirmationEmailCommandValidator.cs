using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.Common.ValueObjects;

namespace SGCM.Data.Application.Identity.Commands.ResendConfirmationEmail;

/// <summary>
/// Rechaza el reenvío antes de llegar al handler si el correo no es válido, aplicando
/// la regla del value object <see cref="Email"/>.
/// </summary>
public sealed class ResendConfirmationEmailCommandValidator : AbstractValidator<ResendConfirmationEmailCommand>
{
    public ResendConfirmationEmailCommandValidator()
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
