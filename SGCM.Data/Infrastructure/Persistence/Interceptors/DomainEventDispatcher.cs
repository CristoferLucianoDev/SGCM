using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common.Interfaces;
using Serilog;

namespace SGCM.Data.Infrastructure.Persistence.Interceptors;

internal sealed class DomainEventDispatcher(IServiceProvider services, ILogger logger)
{
    private readonly ILogger _logger = logger.ForContext<DomainEventDispatcher>();

    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents)
    {
        foreach (var domainEvent in domainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;

            foreach (var handler in services.GetServices(handlerType))
            {
                try
                {
                    await (Task)handleMethod.Invoke(handler, [domainEvent, CancellationToken.None])!;
                }
                catch (Exception exception)
                {
                    // Los datos ya están confirmados: un efecto secundario fallido se registra y no se propaga.
                    _logger.Error(
                        exception,
                        "Falló {Handler} al procesar {Event}",
                        handler!.GetType().Name,
                        domainEvent.GetType().Name);
                }
            }
        }
    }
}