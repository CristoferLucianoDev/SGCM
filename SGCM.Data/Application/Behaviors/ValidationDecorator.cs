using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Behaviors;

/// <summary>
/// Ejecuta los validadores del comando antes del handler. Si alguno falla, devuelve
/// el primer error como Error.Validation y el handler no se ejecuta.
/// </summary>
internal sealed class ValidationDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        foreach (var validator in validators)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                var failure = validation.Errors[0];
                return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
            }
        }

        return await inner.HandleAsync(command, cancellationToken);
    }
}