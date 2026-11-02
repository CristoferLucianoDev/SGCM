using FluentValidation;
using SGCM.Data.Application.Identity.Commands.RefreshSession;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.Logout;

/// <summary>
/// Rechaza refresh tokens vacíos o demasiado largos antes de calcular su hash y abrir la transacción.
/// </summary>
public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(command => command.RefreshToken)
            .NotEmpty()
            .WithErrorCode(UserErrors.RefreshTokenRequired.Code)
            .WithMessage(UserErrors.RefreshTokenRequired.Description)
            .MaximumLength(RefreshSessionCommandValidator.MaxRefreshTokenLength)
            .WithErrorCode(UserErrors.RefreshTokenRequired.Code)
            .WithMessage(UserErrors.RefreshTokenRequired.Description);
    }
}
