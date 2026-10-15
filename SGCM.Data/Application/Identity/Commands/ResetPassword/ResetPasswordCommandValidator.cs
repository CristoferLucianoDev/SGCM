using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.ResetPassword;

/// <summary>
/// Rechaza tokens vacíos o demasiado largos antes de calcular su hash, y contraseñas que no
/// cumplen la política de <see cref="Password"/>.
/// </summary>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    // Igual que en ConfirmEmailCommandValidator, donde la constante es privada.
    private const int MaxTokenLength = 256;

    public ResetPasswordCommandValidator()
    {
        RuleFor(command => command.Token)
            .NotEmpty()
            .WithErrorCode(UserErrors.TokenInvalid.Code)
            .WithMessage(UserErrors.TokenInvalid.Description)
            .MaximumLength(MaxTokenLength)
            .WithErrorCode(UserErrors.TokenInvalid.Code)
            .WithMessage(UserErrors.TokenInvalid.Description);

        RuleFor(command => command.NewPassword).Custom((password, context) =>
            AddFailureIfInvalid(context, Password.Create(password)));
    }

    private static void AddFailureIfInvalid(ValidationContext<ResetPasswordCommand> context, Result result)
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
