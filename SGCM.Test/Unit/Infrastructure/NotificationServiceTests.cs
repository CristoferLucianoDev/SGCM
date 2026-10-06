using NSubstitute;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Infrastructure.Notifications;
using SGCM.Data.Infrastructure.Notifications.Channels;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public class NotificationServiceTests
{
    private static readonly Recipient Recipient = new("ana@correo.com", "Ana");

    private readonly NotificationTemplateStore _templates = new(
        typeof(NotificationServiceTests).Assembly,
        "SGCM.Test.Unit.Infrastructure.Templates.");

    private readonly INotificationChannel _channel = Substitute.For<INotificationChannel>();

    public NotificationServiceTests()
    {
        _channel.DeliverAsync(Arg.Any<RenderedMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
    }

    [Fact]
    public async Task SendAsync_entrega_el_correo_renderizado_al_canal()
    {
        var service = CreateService(_channel);

        var result = await service.SendAsync(new TestNotification(Recipient, "Ana"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _channel.Received(1).DeliverAsync(
            Arg.Is<RenderedMessage>(message =>
                message.Recipient == Recipient
                && message.Subject == "Asunto de prueba"
                && message.HtmlBody.Contains("<p>Hola Ana</p>")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_escapa_los_valores_en_el_cuerpo()
    {
        var service = CreateService(_channel);

        await service.SendAsync(new TestNotification(Recipient, "<b>Ana</b>"), CancellationToken.None);

        await _channel.Received(1).DeliverAsync(
            Arg.Is<RenderedMessage>(message => message.HtmlBody.Contains("&lt;b&gt;Ana&lt;/b&gt;")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_entrega_por_todos_los_canales()
    {
        var other = Substitute.For<INotificationChannel>();
        other.DeliverAsync(Arg.Any<RenderedMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        var service = CreateService(_channel, other);

        await service.SendAsync(new TestNotification(Recipient, "Ana"), CancellationToken.None);

        await _channel.Received(1).DeliverAsync(Arg.Any<RenderedMessage>(), Arg.Any<CancellationToken>());
        await other.Received(1).DeliverAsync(Arg.Any<RenderedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_devuelve_el_fallo_del_canal_sin_lanzar_excepcion()
    {
        var error = Error.Failure("Notification.DeliveryFailed", "SMTP no disponible");
        _channel.DeliverAsync(Arg.Any<RenderedMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure(error)));
        var service = CreateService(_channel);

        var result = await service.SendAsync(new TestNotification(Recipient, "Ana"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }

    private NotificationService CreateService(params INotificationChannel[] channels) =>
        new(_templates, new TemplateRenderer(), channels, Serilog.Core.Logger.None);

    private sealed record TestNotification(Recipient Recipient, string Name) : Notification(Recipient)
    {
        public override string TemplateKey => "test-template";

        public override IReadOnlyDictionary<string, string> GetValues() =>
            new Dictionary<string, string> { ["Name"] = Name };
    }
}