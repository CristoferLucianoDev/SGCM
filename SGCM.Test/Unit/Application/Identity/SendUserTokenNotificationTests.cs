using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Identity;
using SGCM.Data.Application.Identity.EventHandlers;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Identity.Users.Events;
using SGCM.Data.Domain.Common;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class SendUserTokenNotificationTests
{
    private const string ConfirmationUrl = "https://sgcm.test/cuenta/confirmar";
    private const string Email = "paciente@correo.com";
    private const int ConfirmationMinutes = 15;

    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    private readonly SendUserTokenNotification _handler;

    public SendUserTokenNotificationTests()
    {
        var options = Options.Create(new AccountsOptions
        {
            EmailConfirmationUrl = ConfirmationUrl,
            EmailConfirmationTokenMinutes = ConfirmationMinutes
        });

        _handler = new SendUserTokenNotification(_notificationService, options);
    }

    [Fact]
    public async Task Handle_EmailConfirmationToken_SendsConfirmationWithLink()
    {
        AccountConfirmationNotification? sent = null;
        _notificationService.SendAsync(Arg.Do<Notification>(n => sent = n as AccountConfirmationNotification), Arg.Any<CancellationToken>())
        .Returns(Result.Success());

        await _handler.HandleAsync(new UserTokenIssuedEvent(Email, UserTokenType.EmailConfirmation, "raw-token"), CancellationToken.None);

        sent.ShouldNotBeNull();
        sent.Recipient.Email.ShouldBe(Email);
        sent.ConfirmationUrl.ShouldBe($"{ConfirmationUrl}?token=raw-token");
        sent.ExpirationMinutes.ShouldBe(ConfirmationMinutes);
    }

    [Fact]
    public async Task Handle_TokenWithReservedCharacters_EscapesItInLink()
    {
        AccountConfirmationNotification? sent = null;
        _notificationService.SendAsync(Arg.Do<Notification>(n => sent = n as AccountConfirmationNotification), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _handler.HandleAsync(new UserTokenIssuedEvent(Email, UserTokenType.EmailConfirmation, "a+b/c"), CancellationToken.None);

        sent!.ConfirmationUrl.ShouldBe($"{ConfirmationUrl}?token=a%2Bb%2Fc");
    }

    [Fact]
    public async Task Handle_OtherTokenType_DoesNotSend()
    {
        await _handler.HandleAsync(new UserTokenIssuedEvent(Email, UserTokenType.PasswordReset, "raw-token"), CancellationToken.None);

        await _notificationService.DidNotReceive().SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }
}