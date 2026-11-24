using System.Net;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Infrastructure.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

/// <summary>
/// Pruebas de la notificación de solicitud de cita y de su plantilla real.
/// </summary>
public sealed class AppointmentRequestedTemplateTests
{
    private static readonly Recipient Doctor = new("medico@correo.com", "Luis Pérez");

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
    public void Los_valores_llevan_la_fecha_como_dd_MM_yyyy_y_las_horas_en_24_horas()
    {
        var values = new AppointmentRequestedNotification(Doctor, Data with { StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(16, 0) }, 48)
            .GetValues();

        values["AppointmentDate"].ShouldBe("05/01/2027");
        values["StartTime"].ShouldBe("14:00");
        values["EndTime"].ShouldBe("16:00");
        values["ConfirmationDeadlineHours"].ShouldBe("48");
        values["DoctorName"].ShouldBe("Luis Pérez");
        values["PatientName"].ShouldBe("Ana Pérez");
        values["SpecialtyName"].ShouldBe("Cardiología");
        values["ReasonForVisit"].ShouldBe("Control anual");
    }

    [Fact]
    public void Sin_motivo_el_correo_lo_indica()
    {
        new AppointmentRequestedNotification(Doctor, Data with { ReasonForVisit = null }, 48)
            .GetValues()["ReasonForVisit"].ShouldBe(AppointmentRequestedNotification.NoReason);
    }

    [Fact]
    public void La_plantilla_tiene_asunto()
    {
        _templates.Get("appointment-requested").Subject.ShouldBe("Nueva solicitud de cita");
    }

    [Fact]
    public void El_cuerpo_incluye_paciente_especialidad_fecha_horas_motivo_y_plazo()
    {
        var body = Render(new AppointmentRequestedNotification(Doctor, Data, 48));

        // El renderizador codifica en HTML los valores, incluidas las letras con tilde.
        body.ShouldContain(WebUtility.HtmlEncode("Hola, Luis Pérez") + ":");
        body.ShouldContain(WebUtility.HtmlEncode("Ana Pérez") + " solicitó una cita de " + WebUtility.HtmlEncode("Cardiología"));
        body.ShouldContain("05/01/2027");
        body.ShouldContain("09:00 a 11:00");
        body.ShouldContain("Control anual");
        body.ShouldContain("hasta 48 horas antes");
    }

    [Fact]
    public void El_cuerpo_no_deja_variables_sin_reemplazar()
    {
        TemplatePlaceholders.Extract(Render(new AppointmentRequestedNotification(Doctor, Data, 48))).ShouldBeEmpty();
    }

    [Fact]
    public void El_cuerpo_codifica_en_HTML_lo_que_escribio_el_paciente()
    {
        var body = Render(new AppointmentRequestedNotification(Doctor, Data with { ReasonForVisit = "<b>dolor</b> & fiebre" }, 48));

        body.ShouldContain("&lt;b&gt;dolor&lt;/b&gt; &amp; fiebre");
        body.ShouldNotContain("<b>dolor</b>");
    }

    private string Render(AppointmentRequestedNotification notification)
    {
        var template = _templates.Get(notification.TemplateKey);

        return _renderer.Render(template.HtmlBody, notification.GetValues(), htmlEncode: true);
    }
}
