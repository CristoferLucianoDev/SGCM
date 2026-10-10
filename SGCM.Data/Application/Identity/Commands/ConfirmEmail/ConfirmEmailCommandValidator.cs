using FluentValidation;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.ConfirmEmail;

/// <summary>
/// Rechaza tokens vacíos o demasiado largos antes de calcular su hash.
/// </summary>
public sealed class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    private const int MaxTokenLength = 256;

    public ConfirmEmailCommandValidator()
    {
        RuleFor(command => command.Token)
            .NotEmpty()
            .WithErrorCode(UserErrors.TokenInvalid.Code)
            .WithMessage(UserErrors.TokenInvalid.Description)
            .MaximumLength(MaxTokenLength)
            .WithErrorCode(UserErrors.TokenInvalid.Code)
            .WithMessage(UserErrors.TokenInvalid.Description);
    }
}
