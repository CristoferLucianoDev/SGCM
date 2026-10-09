using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.RegisterPatientAccount;

/// <summary>
/// Rechaza el registro antes de llegar al handler si el correo o la contraseña no son válidos.
/// Las reglas viven en los value objects; el validador solo las aplica para fallar pronto.
/// </summary>
public sealed class RegisterPatientAccountCommandValidator : AbstractValidator<RegisterPatientAccountCommand>
{
    public RegisterPatientAccountCommandValidator()
    {
        RuleFor(command => command.Email).Custom((email, context) =>
            AddFailureIfInvalid(context, Email.Create(email)));

        RuleFor(command => command.Password).Custom((password, context) =>
            AddFailureIfInvalid(context, Password.Create(password)));
    }

    private static void AddFailureIfInvalid(ValidationContext<RegisterPatientAccountCommand> context, Result result)
    {
        if (result.IsSuccess)
        {
            return;
        }

        context.AddFailure(new ValidationFailure(context.PropertyPath, result.Error.Description)
        {
            ErrorCode = result.Error.Code
        });
    }
}