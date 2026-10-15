using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Infrastructure.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

/// <summary>
/// Pruebas de la plantilla real del correo de restablecimiento de contraseña.
/// </summary>
public class PasswordResetTemplateTests
{
    private const string ResetUrl = "https://localhost:7126/cuenta/restablecer?token=ABC123";
    private const int ExpirationMinutes = 10;

    private static readonly Recipient Recipient = new("paciente@correo.com", "paciente@correo.com");

    private readonly NotificationTemplateStore _templates = new();
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void La_plantilla_tiene_asunto()
    {
        var template = _templates.Get(CreateNotification(ResetUrl).TemplateKey);

        template.Subject.ShouldBe("Restablece tu contraseña");
    }

    [Fact]
    public void El_cuerpo_incluye_el_enlace_y_la_vigencia()
    {
        var body = Render(CreateNotification(ResetUrl));

        body.ShouldContain(ResetUrl);
        body.ShouldContain($"{ExpirationMinutes} minutos");
    }

    [Fact]
    public void El_cuerpo_no_deja_variables_sin_reemplazar()
    {
        var body = Render(CreateNotification(ResetUrl));

        TemplatePlaceholders.Extract(body).ShouldBeEmpty();
    }

    [Fact]
    public void El_cuerpo_codifica_en_HTML_los_caracteres_especiales_del_enlace()
    {
        var body = Render(CreateNotification("https://localhost:7126/cuenta/restablecer?token=a&b"));

        body.ShouldContain("a&amp;b");
        body.ShouldNotContain("token=a&b");
    }

    private static PasswordResetNotification CreateNotification(string resetUrl) =>
        new(Recipient, resetUrl, ExpirationMinutes);

    private string Render(PasswordResetNotification notification)
    {
        var template = _templates.Get(notification.TemplateKey);

        return _renderer.Render(template.HtmlBody, notification.GetValues(), htmlEncode: true);
    }
}