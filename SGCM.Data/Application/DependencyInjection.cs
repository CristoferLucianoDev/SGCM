using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Behaviors;

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

        return services;
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
}