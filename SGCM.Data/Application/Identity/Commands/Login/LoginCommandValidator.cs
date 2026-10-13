using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.Login;

/// <summary>
/// Rechaza el inicio de sesión antes de llegar al handler si el correo no es válido o la
/// contraseña supera el largo máximo.
/// </summary>
/// <remarks>
/// No se aplican las reglas de contraseña nueva, porque una contraseña guardada antes de
/// cambiar las reglas debe poder usarse; el tope evita calcular hashes de textos enormes.
/// Una contraseña vacía no se valida aquí: llega al handler y es una credencial inválida más.
/// </remarks>
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(command => command.Email).Custom((email, context) =>
            AddFailureIfInvalid(context, Email.Create(email)));

        RuleFor(command => command.Password)
            .MaximumLength(Password.MaxLength)
            .WithErrorCode(UserErrors.PasswordTooLong.Code)
            .WithMessage(UserErrors.PasswordTooLong.Description);
    }

    private static void AddFailureIfInvalid(ValidationContext<LoginCommand> context, Result result)
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
