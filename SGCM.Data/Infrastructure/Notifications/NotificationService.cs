using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Infrastructure.Notifications.Channels;
using Serilog;

namespace SGCM.Data.Infrastructure.Notifications;

internal sealed class NotificationService(
    NotificationTemplateStore templates,
    ITemplateRenderer renderer,
    IEnumerable<INotificationChannel> channels,
    ILogger logger) : INotificationService
{
    private readonly ILogger _logger = logger.ForContext<NotificationService>();

    public async Task<Result> SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        var template = templates.Get(notification.TemplateKey);
        var values = notification.GetValues();

        var message = new RenderedMessage(
            notification.Recipient,
            renderer.Render(template.Subject, values, htmlEncode: false),
            renderer.Render(template.HtmlBody, values, htmlEncode: true));

        foreach (var channel in channels)
        {
            var result = await channel.DeliverAsync(message, cancellationToken);
            if (result.IsFailure)
            {
                _logger.Error(
                    "No se pudo enviar {TemplateKey} por {Channel}: {Error}",
                    notification.TemplateKey,
                    channel.GetType().Name,
                    result.Error.Description);

                return result;
            }
        }

        return Result.Success();
    }
}