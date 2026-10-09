using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Notifications;
using SGCM.Data.Infrastructure.Notifications.Channels;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Interceptors;
using SGCM.Data.Infrastructure.Persistence.Repositories.Identity;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Infrastructure.Security;
using SGCM.Data.Infrastructure.Services;

namespace SGCM.Data.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddPersistence();
        services.AddNotifications();
        services.AddSecurity();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return services;
    }

    private static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddSingleton(_ => new NotificationTemplateStore());
        services.AddSingleton<ITemplateRenderer, TemplateRenderer>();

        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationChannel, EmailNotificationChannel>();
        services.AddScoped<IEmailTransport, SmtpEmailTransport>();

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<DomainEventsQueue>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped<DomainEventsInterceptor>();

        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.SgcmDatabase)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DomainEventsInterceptor>()));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();

        return services;
    }

    private static IServiceCollection AddSecurity(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();

        return services;
    }
}