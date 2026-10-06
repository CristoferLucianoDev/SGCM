using SGCM.Data.Domain.Common;

namespace SGCM.Data.Infrastructure.Notifications.Channels;

internal interface INotificationChannel
{
    Task<Result> DeliverAsync(RenderedMessage message, CancellationToken cancellationToken);
}