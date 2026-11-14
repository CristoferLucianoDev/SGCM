using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.ChangePassword;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(command => command.CurrentPassword)
            .NotEmpty()
            .WithErrorCode(UserErrors.CurrentPasswordRequired.Code)
            .WithMessage(UserErrors.CurrentPasswordRequired.Description);

        RuleFor(command => command.NewPassword).Custom((password, context) =>
        {
            var result = Password.Create(password);
            if (result.IsFailure)
                context.AddFailure(new ValidationFailure(context.PropertyPath, result.Error.Description)
                { ErrorCode = result.Error.Code });
        });
    }
}
