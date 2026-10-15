using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Identity.Users.Events;

namespace SGCM.Data.Application.Identity.EventHandlers;

/// <summary>
/// Envía el correo con el enlace del token emitido, según su tipo. Se ejecuta después del
/// commit, así que si la operación se revierte no se envía nada.
/// </summary>
internal sealed class SendUserTokenNotification(
    INotificationService notificationService,
    IOptions<AccountsOptions> accountsOptions) : IDomainEventHandler<UserTokenIssuedEvent>
{
    public async Task HandleAsync(UserTokenIssuedEvent domainEvent, CancellationToken cancellationToken)
    {
        var notification = BuildNotification(domainEvent, accountsOptions.Value);

        // Los tipos de token que todavía no tienen correo se envían desde sus propios PBIs.
        if (notification is null)
        {
            return;
        }

        await notificationService.SendAsync(notification, cancellationToken);
    }

    private static Notification? BuildNotification(UserTokenIssuedEvent domainEvent, AccountsOptions options)
    {
        var recipient = new Recipient(domainEvent.Email, domainEvent.Email);

        return domainEvent.Type switch
        {
            UserTokenType.EmailConfirmation => new AccountConfirmationNotification(
                recipient,
                BuildLink(options.EmailConfirmationUrl, domainEvent.RawToken),
                options.EmailConfirmationTokenMinutes),
            UserTokenType.PasswordReset => new PasswordResetNotification(
                recipient,
                BuildLink(options.PasswordResetUrl, domainEvent.RawToken),
                options.PasswordResetTokenMinutes),
            _ => null
        };
    }

    private static string BuildLink(string baseUrl, string rawToken) =>
        $"{baseUrl}?token={Uri.EscapeDataString(rawToken)}";
}