namespace SGCM.Data.Infrastructure.Notifications.Channels;

internal interface IEmailTransport
{
    Task SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken cancellationToken);
}