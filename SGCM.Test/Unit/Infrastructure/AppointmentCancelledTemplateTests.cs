using System.Net;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

/// <summary>
/// Pruebas de la notificación de cancelación de cita y de su plantilla real.
/// </summary>
public sealed class AppointmentCancelledTemplateTests
{
    private const string Reason = "Tengo un viaje";

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
        values["DoctorName"].ShouldBe("Luis Pérez");
        values["PatientName"].ShouldBe("Ana Pérez");
        values["SpecialtyName"].ShouldBe("Cardiología");
        values["CancellationReason"].ShouldBe(Reason);
    }

    [Fact]
    public void Sin_motivo_el_correo_lo_indica()
    {
        Create(Data, null).GetValues()["CancellationReason"].ShouldBe(AppointmentCancelledNotification.NoReason);
    }

    [Fact]
    public void Si_cancela_el_paciente_el_correo_va_al_medico_con_la_plantilla_del_paciente()
    {
        var notification = Create(Data, Reason);

        notification.Recipient.ShouldBe(new Recipient("medico@correo.com", "Luis Pérez"));
        notification.TemplateKey.ShouldBe(AppointmentCancelledNotification.CancelledByPatientTemplateKey);
        notification.TemplateKey.ShouldBe("appointment-cancelled-by-patient");
        notification.CancelledBy.ShouldBe(AppointmentCanceller.Patient);
    }

    [Fact]
    public void Un_argumento_invalido_es_un_error_de_programacion()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            AppointmentCancelledNotification.Create(Data, (AppointmentCanceller)99, Reason));
        Should.Throw<ArgumentNullException>(() =>
            AppointmentCancelledNotification.Create(null!, AppointmentCanceller.Patient, Reason));
    }

    [Fact]
    public void La_plantilla_tiene_asunto()
    {
        _templates.Get(AppointmentCancelledNotification.CancelledByPatientTemplateKey).Subject
            .ShouldBe("Cita cancelada por el paciente");
    }

    [Fact]
    public void El_cuerpo_incluye_medico_paciente_especialidad_fecha_horas_y_motivo()
    {
        var body = Render(Create(Data, "Viaje"));

        // El renderizador codifica en HTML los valores, incluidas las letras con tilde.
        body.ShouldContain(WebUtility.HtmlEncode("Hola, Luis Pérez") + ":");
        body.ShouldContain(WebUtility.HtmlEncode("Ana Pérez") + " canceló su cita de "
            + WebUtility.HtmlEncode("Cardiología"));
        body.ShouldContain("05/01/2027");
        body.ShouldContain("09:00 a 11:00");
        body.ShouldContain("Viaje");
    }

    [Fact]
    public void El_cuerpo_no_deja_variables_sin_reemplazar()
    {
        TemplatePlaceholders.Extract(Render(Create(Data, Reason))).ShouldBeEmpty();
    }

    [Fact]
    public void El_cuerpo_codifica_en_HTML_el_motivo_que_escribio_el_paciente()
    {
        var body = Render(Create(Data, "<b>viaje</b> & más"));

        body.ShouldContain("&lt;b&gt;viaje&lt;/b&gt; &amp; " + WebUtility.HtmlEncode("más"));
        body.ShouldNotContain("<b>viaje</b>");
    }

    [Fact]
    public void Si_cancela_el_medico_el_correo_va_al_paciente_con_la_plantilla_del_paciente()
    {
        var notification = CreateByDoctor(Data, Reason);

        notification.Recipient.ShouldBe(new Recipient("paciente@correo.com", "Ana Pérez"));
        notification.TemplateKey.ShouldBe(AppointmentCancelledNotification.CancelledForPatientTemplateKey);
        notification.TemplateKey.ShouldBe("appointment-cancelled-for-patient");
        notification.CancelledBy.ShouldBe(AppointmentCanceller.Doctor);
    }

    [Fact]
    public void La_plantilla_del_paciente_tiene_asunto()
    {
        _templates.Get(AppointmentCancelledNotification.CancelledForPatientTemplateKey).Subject
            .ShouldBe("Su cita fue cancelada");
    }

    [Fact]
    public void El_cuerpo_del_paciente_incluye_saludo_medico_hora_motivo_fecha_y_especialidad()
    {
        var body = Render(CreateByDoctor(Data, "Emergencia familiar"));

        // El texto fijo de la plantilla va sin codificar; los valores sí se codifican en HTML.
        body.ShouldContain(WebUtility.HtmlEncode("Hola, Ana Pérez") + ":");
        body.ShouldContain("Su cita con el doctor " + WebUtility.HtmlEncode("Luis Pérez")
            + " a las 09:00 fue cancelada por el siguiente motivo:");
        body.ShouldContain("Emergencia familiar");
        body.ShouldContain("05/01/2027");
        body.ShouldContain(WebUtility.HtmlEncode("Cardiología"));
    }

    [Fact]
    public void El_cuerpo_del_paciente_no_deja_variables_sin_reemplazar()
    {
        TemplatePlaceholders.Extract(Render(CreateByDoctor(Data, Reason))).ShouldBeEmpty();
    }

    [Fact]
    public void El_cuerpo_del_paciente_codifica_en_HTML_el_motivo_que_escribio_el_medico()
    {
        var body = Render(CreateByDoctor(Data, "<b>urgencia</b> & más"));

        body.ShouldContain("&lt;b&gt;urgencia&lt;/b&gt; &amp; " + WebUtility.HtmlEncode("más"));
        body.ShouldNotContain("<b>urgencia</b>");
    }

    private static AppointmentCancelledNotification CreateByDoctor(AppointmentNotificationData data, string? reason) =>
        AppointmentCancelledNotification.Create(data, AppointmentCanceller.Doctor, reason);

    private static AppointmentCancelledNotification Create(AppointmentNotificationData data, string? reason) =>
        AppointmentCancelledNotification.Create(data, AppointmentCanceller.Patient, reason);

    private string Render(AppointmentCancelledNotification notification)
    {
        var template = _templates.Get(notification.TemplateKey);

        return _renderer.Render(template.HtmlBody, notification.GetValues(), htmlEncode: true);
    }
}
