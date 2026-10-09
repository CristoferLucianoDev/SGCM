using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Infrastructure.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public class AccountConfirmationTemplateTests
{
    private readonly NotificationTemplateStore _templates = new();
    private readonly TemplateRenderer _renderer = new();

    private readonly AccountConfirmationNotification _notification = new(
        new Recipient("paciente@correo.com", "paciente@correo.com"),
        "https://localhost:7126/account/confirm?token=ABC123",
        15);

    [Fact]
    public void La_plantilla_tiene_asunto()
    {
        var template = _templates.Get(_notification.TemplateKey);

        template.Subject.ShouldBe("Confirma tu cuenta");
    }

    [Fact]
    public void El_cuerpo_incluye_el_enlace_y_la_vigencia()
    {
        var body = Render();

        body.ShouldContain("https://localhost:7126/account/confirm?token=ABC123");
        body.ShouldContain("15 minutos");
    }

    [Fact]
    public void El_cuerpo_no_deja_variables_sin_reemplazar()
    {
        var body = Render();

        TemplatePlaceholders.Extract(body).ShouldBeEmpty();
    }

    private string Render()
    {
        var template = _templates.Get(_notification.TemplateKey);

        return _renderer.Render(template.HtmlBody, _notification.GetValues(), htmlEncode: true);
    }
}