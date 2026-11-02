using FluentValidation;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.RefreshSession;

/// <summary>
/// Rechaza refresh tokens vacíos o demasiado largos antes de calcular su hash y abrir la transacción.
/// </summary>
public sealed class RefreshSessionCommandValidator : AbstractValidator<RefreshSessionCommand>
{
    /// <summary>
    /// Longitud máxima admitida para un refresh token recibido. Los emitidos son mucho más cortos.
    /// </summary>
    public const int MaxRefreshTokenLength = 256;

    public RefreshSessionCommandValidator()
    {
        RuleFor(command => command.RefreshToken)
            .NotEmpty()
            .WithErrorCode(UserErrors.RefreshTokenRequired.Code)
            .WithMessage(UserErrors.RefreshTokenRequired.Description)
            .MaximumLength(MaxRefreshTokenLength)
            .WithErrorCode(UserErrors.RefreshTokenRequired.Code)
            .WithMessage(UserErrors.RefreshTokenRequired.Description);
    }
}
