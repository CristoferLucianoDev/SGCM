using System.Net;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Infrastructure.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

/// <summary>
/// Pruebas de la notificación de cita rechazada y de su plantilla real.
/// </summary>
public sealed class AppointmentRejectedTemplateTests
{
    private const string Reason = "Agenda completa";

    private static readonly AppointmentNotificationData Data = new(
        99,
        "Ana Pérez",
        "paciente@correo.com",
        "Luis Pérez",
        "medico@correo.com",
        "Cardiología",
        1500m,
        new DateOnly(2027, 1, 5),
        new TimeOnly(9, 0),
        new TimeOnly(11, 0),
        "Control anual");

    private readonly NotificationTemplateStore _templates = new();
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void Los_valores_llevan_la_fecha_como_dd_MM_yyyy_las_horas_en_24_horas_y_el_motivo()
    {
        var values = Create(Data with { StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(16, 0) }, Reason)
            .GetValues();

        values["AppointmentDate"].ShouldBe("05/01/2027");
        values["StartTime"].ShouldBe("14:00");
        values["EndTime"].ShouldBe("16:00");
        values["PatientName"].ShouldBe("Ana Pérez");
        values["DoctorName"].ShouldBe("Luis Pérez");
        values["SpecialtyName"].ShouldBe("Cardiología");
        values["RejectionReason"].ShouldBe(Reason);
    }

    [Fact]
    public void Sin_motivo_el_correo_lo_indica()
    {
        Create(Data, null).GetValues()["RejectionReason"].ShouldBe(AppointmentRejectedNotification.NoReason);
    }

    [Fact]
    public void La_notificacion_usa_la_clave_de_la_plantilla_de_rechazo()
    {
        var notification = Create(Data, Reason);

        notification.TemplateKey.ShouldBe(AppointmentRejectedNotification.RejectedTemplateKey);
        notification.TemplateKey.ShouldBe("appointment-rejected");
        notification.Recipient.ShouldBe(new Recipient("paciente@correo.com", "Ana Pérez"));
    }

    [Fact]
    public void La_plantilla_tiene_asunto()
    {
        _templates.Get(AppointmentRejectedNotification.RejectedTemplateKey).Subject
            .ShouldBe("Su solicitud de cita fue rechazada");
    }

    [Fact]
    public void El_cuerpo_incluye_saludo_medico_especialidad_motivo_fecha_y_horas()
    {
        var body = Render(Create(Data, Reason));

        // El texto fijo de la plantilla va sin codificar; los valores sí se codifican en HTML.
        body.ShouldContain(WebUtility.HtmlEncode("Hola, Ana Pérez") + ":");
        body.ShouldContain("El doctor " + WebUtility.HtmlEncode("Luis Pérez") + " rechazó su solicitud de cita de "
            + WebUtility.HtmlEncode("Cardiología") + ".");
        body.ShouldContain("Motivo del rechazo:</strong> " + WebUtility.HtmlEncode(Reason));
        body.ShouldContain("05/01/2027");
        body.ShouldContain("09:00 a 11:00");
    }

    [Fact]
    public void El_cuerpo_no_deja_variables_sin_reemplazar()
    {
        TemplatePlaceholders.Extract(Render(Create(Data, Reason))).ShouldBeEmpty();
    }

    [Fact]
    public void El_cuerpo_codifica_en_HTML_el_motivo_que_escribio_el_medico()
    {
        var body = Render(Create(Data, "<b>urgencia</b> & más"));

        body.ShouldContain("&lt;b&gt;urgencia&lt;/b&gt; &amp; " + WebUtility.HtmlEncode("más"));
        body.ShouldNotContain("<b>urgencia</b>");
    }

    private static AppointmentRejectedNotification Create(AppointmentNotificationData data, string? reason) =>
        new(new Recipient(data.PatientEmail, data.PatientName), data, reason);

    private string Render(AppointmentRejectedNotification notification)
    {
        var template = _templates.Get(notification.TemplateKey);

        return _renderer.Render(template.HtmlBody, notification.GetValues(), htmlEncode: true);
    }
}
