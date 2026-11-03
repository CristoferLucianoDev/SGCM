using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.ChangeTemporaryPassword;

public sealed class ChangeTemporaryPasswordCommandValidator : AbstractValidator<ChangeTemporaryPasswordCommand>
{
    public ChangeTemporaryPasswordCommandValidator()
    {
        RuleFor(command => command.NewPassword).Custom((password, context) =>
        {
            var result = Password.Create(password);
            if (result.IsFailure)
                context.AddFailure(new ValidationFailure(context.PropertyPath, result.Error.Description)
                { ErrorCode = result.Error.Code });
        });
    }
}

