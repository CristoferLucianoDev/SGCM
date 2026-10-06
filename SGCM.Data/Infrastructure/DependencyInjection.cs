using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Infrastructure.Notifications;
using SGCM.Data.Infrastructure.Notifications.Channels;

namespace SGCM.Data.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddNotifications();

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
}