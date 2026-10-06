using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Infrastructure.Notifications.Channels;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public class EmailNotificationChannelTests
{
    private static readonly RenderedMessage Message =
        new(new Recipient("ana@correo.com", "Ana"), "Asunto", "<p>Cuerpo</p>");

    private readonly IEmailTransport _transport = Substitute.For<IEmailTransport>();

    [Fact]
    public async Task DeliverAsync_envia_el_correo_por_el_transporte()
    {
        var channel = new EmailNotificationChannel(_transport);

        var result = await channel.DeliverAsync(Message, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _transport.Received(1).SendAsync("ana@correo.com", "Ana", "Asunto", "<p>Cuerpo</p>", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliverAsync_convierte_un_fallo_del_transporte_en_Result_fallido()
    {
        _transport.SendAsync(default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("SMTP no disponible")));
        var channel = new EmailNotificationChannel(_transport);

        var result = await channel.DeliverAsync(Message, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Notification.DeliveryFailed");
    }

    [Fact]
    public async Task DeliverAsync_no_atrapa_la_cancelacion()
    {
        _transport.SendAsync(default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException(new OperationCanceledException()));
        var channel = new EmailNotificationChannel(_transport);

        await Should.ThrowAsync<OperationCanceledException>(() => channel.DeliverAsync(Message, CancellationToken.None));
    }
}