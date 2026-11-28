using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Behaviors;
using SGCM.Data.Application.Clinical;
using SGCM.Data.Application.Identity.EventHandlers;
using SGCM.Data.Application.Messaging;
using SGCM.Data.Application.Messaging.ReminderTemplates;
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
        services.AddScoped<IDoctorAssignmentService, DoctorAssignmentService>();
        services.AddScoped<IAffectedAppointmentsPolicy, AffectedAppointmentsPolicy>();
        services.AddScoped<IAffectedAppointmentsCanceler, AffectedAppointmentsCanceler>();
        services.AddScoped<DoctorAppointmentAccess>();
        services.AddScoped<AppointmentSlotChecker>();
        services.AddScoped<IAppointmentReportBuilder, AppointmentReportBuilder>();
        services.AddScoped<MedicalRecordAccessTracker>();
        services.AddScoped<MedicalHistoryAccessPolicy>();
        services.AddScoped<ReminderComposer>();
        services.AddScoped<AutomaticReminderScheduler>();
        services.AddScoped<DoctorReminderTemplateAccess>();
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

    // Orden de ejecución: validación, luego transacción, el registro de accesos si el comando lo pide y por último
    // el handler. El registro de accesos queda dentro de la transacción para guardarse junto con el cambio clínico.
    private static object Decorate(IServiceProvider provider, Type implementation, Type service)
    {
        var arguments = service.GetGenericArguments();
        var handler = provider.GetRequiredService(implementation);
        if (IsAccessLogged(arguments[0]))
            handler = Wrap(provider, typeof(AccessLogDecorator<,>), arguments, handler);
        var transactional = Wrap(provider, typeof(TransactionDecorator<,>), arguments, handler);

        return Wrap(provider, typeof(ValidationDecorator<,>), arguments, transactional);
    }

    private static object Wrap(IServiceProvider provider, Type decorator, Type[] arguments, object inner) =>
        ActivatorUtilities.CreateInstance(provider, decorator.MakeGenericType(arguments), inner);

    /// <summary>
    /// Indica si el comando pide el registro de accesos, es decir, si implementa
    /// <see cref="IAccessLoggedCommand{TResponse}"/>.
    /// </summary>
    internal static bool IsAccessLogged(Type commandType) =>
        commandType.GetInterfaces().Any(contract => contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IAccessLoggedCommand<>));

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
