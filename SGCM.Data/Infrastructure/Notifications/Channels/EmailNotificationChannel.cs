using SGCM.Data.Domain.Common;

namespace SGCM.Data.Infrastructure.Notifications.Channels;

internal sealed class EmailNotificationChannel(IEmailTransport transport) : INotificationChannel
{
    public async Task<Result> DeliverAsync(RenderedMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await transport.SendAsync(
                message.Recipient.Email,
                message.Recipient.Name,
                message.Subject,
                message.HtmlBody,
                cancellationToken);

            return Result.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Error.Failure("Notification.DeliveryFailed", exception.Message);
        }
    }
}