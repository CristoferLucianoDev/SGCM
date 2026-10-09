using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Identity.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Application;

public class AccountConfirmationNotificationTests
{
    private static readonly Recipient Recipient = new("paciente@correo.com", "paciente@correo.com");

    [Fact]
    public void Usa_la_plantilla_de_confirmacion_de_cuenta()
    {
        var notification = new AccountConfirmationNotification(Recipient, "https://sgcm/confirm?token=abc", 15);

        notification.TemplateKey.ShouldBe("account-confirmation");
    }

    [Fact]
    public void GetValues_incluye_el_enlace_y_la_vigencia()
    {
        var notification = new AccountConfirmationNotification(Recipient, "https://sgcm/confirm?token=abc", 15);

        var values = notification.GetValues();

        values["ConfirmationUrl"].ShouldBe("https://sgcm/confirm?token=abc");
        values["ExpirationMinutes"].ShouldBe("15");
    }
}