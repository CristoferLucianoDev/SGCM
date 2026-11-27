using System.Net;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Messaging.Notifications;
using SGCM.Data.Infrastructure.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class AppointmentReminderTemplateTests
{
    private const string Subject = "Recordatorio: su cita de Cardiología es mañana";
    private const string Message = "Hola Ana <Pérez>, le recordamos su cita.\nTraiga sus estudios.";

    private readonly NotificationTemplateStore _templates = new();
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void La_notificacion_usa_la_plantilla_del_recordatorio_y_va_al_paciente()
    {
        var notification = Create();

        notification.TemplateKey.ShouldBe(ReminderNotification.ReminderTemplateKey);
        notification.TemplateKey.ShouldBe("appointment-reminder");
        notification.Recipient.ShouldBe(new Recipient("paciente@correo.com", "Ana Pérez"));
        notification.GetValues().ShouldBe(new Dictionary<string, string>
        {
            ["Subject"] = Subject,
            ["Message"] = Message
        }, ignoreOrder: true);
    }

    [Fact]
    public void El_asunto_del_correo_es_el_del_recordatorio_sin_codificar()
    {
        var template = _templates.Get(ReminderNotification.ReminderTemplateKey);

        _renderer.Render(template.Subject, Create().GetValues(), htmlEncode: false).ShouldBe(Subject);
    }

    [Fact]
    public void El_cuerpo_lleva_el_texto_guardado_codificado_como_HTML()
    {
        var template = _templates.Get(ReminderNotification.ReminderTemplateKey);

        var body = _renderer.Render(template.HtmlBody, Create().GetValues(), htmlEncode: true);

        body.ShouldContain(WebUtility.HtmlEncode(Message));
        body.ShouldNotContain("<Pérez>");
        body.ShouldNotContain("{Message}");
    }

    private static ReminderNotification Create() =>
        new(new Recipient("paciente@correo.com", "Ana Pérez"), Subject, Message);
}
