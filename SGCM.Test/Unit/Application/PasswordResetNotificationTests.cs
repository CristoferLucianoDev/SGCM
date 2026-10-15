using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Identity.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Application;

/// <summary>
/// Pruebas de los datos que <see cref="PasswordResetNotification"/> entrega a su plantilla.
/// </summary>
public class PasswordResetNotificationTests
{
    private const string ResetUrl = "https://sgcm/reset?token=abc";
    private const int ExpirationMinutes = 10;

    private static readonly Recipient Recipient = new("paciente@correo.com", "paciente@correo.com");

    [Fact]
    public void Usa_la_plantilla_de_restablecimiento_de_contrasena()
    {
        var notification = new PasswordResetNotification(Recipient, ResetUrl, ExpirationMinutes);

        notification.TemplateKey.ShouldBe("password-reset");
    }

    [Fact]
    public void GetValues_incluye_el_enlace_y_la_vigencia()
    {
        var notification = new PasswordResetNotification(Recipient, ResetUrl, ExpirationMinutes);

        var values = notification.GetValues();

        values["ResetUrl"].ShouldBe(ResetUrl);
        values["ExpirationMinutes"].ShouldBe("10");
    }
}