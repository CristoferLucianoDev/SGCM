using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace SGCM.Data.Infrastructure.Notifications.Channels;

internal sealed class SmtpEmailTransport(IOptions<SmtpOptions> options) : IEmailTransport
{
    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.SenderName, settings.SenderEmail));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();

        var security = settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(settings.Host, settings.Port, security, cancellationToken);

        if (!string.IsNullOrEmpty(settings.Username))
        {
            await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}