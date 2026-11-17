using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Behaviors;
using SGCM.Data.Application.Identity.EventHandlers;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Scheduling;

namespace SGCM.Data.Application;

public static class DependencyInjection
{
    private static readonly Assembly ApplicationAssembly = typeof(DependencyInjection).Assembly;

    /// <summary>
    /// Registra los handlers de comandos envueltos en sus decoradores y los validadores.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);
        services.AddCommandHandlers();
        services.AddQueryHandlers();
        services.AddDomainEventHandlers();
        services.AddScoped<AvailabilityBlockGenerator>();
        // La zona horaria de la agenda se valida al arrancar, así que el calendario se crea una sola vez.
        services.AddSingleton(provider =>
            BusinessCalendar.ForTimeZone(provider.GetRequiredService<IOptions<SchedulingOptions>>().Value.TimeZone));

        return services;
    }

    private static void AddQueryHandlers(this IServiceCollection services)
    {
        var handlers = ApplicationAssembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
                .Select(contract => (Implementation: type, Service: contract)));

        foreach (var (implementation, service) in handlers)
        {
            services.AddScoped(service, implementation);
        }
    }

    private static void AddCommandHandlers(this IServiceCollection services)
    {
        var handlers = ApplicationAssembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
                .Select(contract => (Implementation: type, Service: contract)));

        foreach (var (implementation, service) in handlers)
        {
            services.AddScoped(implementation);
            services.AddScoped(service, provider => Decorate(provider, implementation, service));
        }
    }

    // Orden de ejecución: validación, luego transacción y por último el handler.
    private static object Decorate(IServiceProvider provider, Type implementation, Type service)
    {
        var arguments = service.GetGenericArguments();
        var handler = provider.GetRequiredService(implementation);
        var transactional = Wrap(provider, typeof(TransactionDecorator<,>), arguments, handler);

        return Wrap(provider, typeof(ValidationDecorator<,>), arguments, transactional);
    }

    private static object Wrap(IServiceProvider provider, Type decorator, Type[] arguments, object inner) =>
        ActivatorUtilities.CreateInstance(provider, decorator.MakeGenericType(arguments), inner);

    private static void AddDomainEventHandlers(this IServiceCollection services)
    {
        var handlers = ApplicationAssembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))
                .Select(contract => (Implementation: type, Service: contract)));

        foreach (var (implementation, service) in handlers)
        {
            services.AddScoped(service, implementation);
        }
    }
}
