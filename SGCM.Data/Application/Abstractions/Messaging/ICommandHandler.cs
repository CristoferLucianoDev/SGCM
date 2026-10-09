using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Abstractions.Messaging;

/// <summary>
/// Ejecuta un comando. Cada comando tiene exactamente un handler.
/// </summary>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    /// <summary>
    /// Ejecuta el caso de uso. Los errores de negocio se devuelven en el Result, no se lanzan.
    /// </summary>
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}