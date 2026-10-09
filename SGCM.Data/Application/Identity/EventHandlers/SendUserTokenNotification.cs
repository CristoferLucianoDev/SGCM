using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Identity.Users.Events;

namespace SGCM.Data.Application.Identity.EventHandlers;

/// <summary>
/// Envía el correo con el enlace del token emitido. Se ejecuta después del commit,
/// así que si el registro se revierte no se envía nada.
/// </summary>
internal sealed class SendUserTokenNotification(
    INotificationService notificationService,
    IOptions<AccountsOptions> accountsOptions) : IDomainEventHandler<UserTokenIssuedEvent>
{
    public async Task HandleAsync(UserTokenIssuedEvent domainEvent, CancellationToken cancellationToken)
    {
        // Los demás tipos de token se envían desde sus propios PBIs.
        if (domainEvent.Type != UserTokenType.EmailConfirmation)
        {
            return;
        }

        var options = accountsOptions.Value;
        var notification = new AccountConfirmationNotification(
            new Recipient(domainEvent.Email, domainEvent.Email),
            BuildLink(options.EmailConfirmationUrl, domainEvent.RawToken),
            options.EmailConfirmationTokenMinutes);

        await notificationService.SendAsync(notification, cancellationToken);
    }

    private static string BuildLink(string baseUrl, string rawToken) =>
        $"{baseUrl}?token={Uri.EscapeDataString(rawToken)}";
}