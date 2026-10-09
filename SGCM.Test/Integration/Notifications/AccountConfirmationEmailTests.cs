using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Infrastructure.Notifications;
using SGCM.Data.Infrastructure.Notifications.Channels;
using Shouldly;

namespace SGCM.Test.Integration.Notifications;

[Trait("Category", "Integration")]
public class AccountConfirmationEmailTests
{
    [Fact]
    public async Task Envia_el_correo_de_confirmacion_al_buzon_local()
    {
        var smtp = Options.Create(new SmtpOptions
        {
            Host = "localhost",
            Port = 54325,
            SenderEmail = "no-reply@sgcm.local",
            SenderName = "Sistema de Gestión de Citas Médicas"
        });
        var service = new NotificationService(
            new NotificationTemplateStore(),
            new TemplateRenderer(),
            [new EmailNotificationChannel(new SmtpEmailTransport(smtp))],
            Serilog.Core.Logger.None);

        var result = await service.SendAsync(
            new AccountConfirmationNotification(
                new Recipient("paciente@correo.com", "paciente@correo.com"),
                "https://localhost:7126/account/confirm?token=ABC123",
                15),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Error.Description);
    }
}