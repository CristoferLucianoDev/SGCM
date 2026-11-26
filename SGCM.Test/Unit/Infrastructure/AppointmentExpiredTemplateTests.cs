using System.Net;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

/// <summary>
/// Pruebas de la notificación de cita vencida y de sus dos plantillas reales.
/// </summary>
public sealed class AppointmentExpiredTemplateTests
{
    private const int DeadlineHours = 48;

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
    public void Los_valores_llevan_la_fecha_como_dd_MM_yyyy_las_horas_en_24_horas_y_el_plazo()
    {
        var values = Create(
                Data with { StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(16, 0) },
                AppointmentParticipant.Patient)
            .GetValues();

        values["AppointmentDate"].ShouldBe("05/01/2027");
        values["StartTime"].ShouldBe("14:00");
        values["EndTime"].ShouldBe("16:00");
        values["PatientName"].ShouldBe("Ana Pérez");
        values["DoctorName"].ShouldBe("Luis Pérez");
        values["SpecialtyName"].ShouldBe("Cardiología");
        values["ConfirmationDeadlineHours"].ShouldBe("48");
    }

    [Fact]
    public void Para_el_paciente_el_correo_va_al_paciente_con_su_plantilla()
    {
        var notification = Create(Data, AppointmentParticipant.Patient);

        notification.Recipient.ShouldBe(new Recipient("paciente@correo.com", "Ana Pérez"));
        notification.TemplateKey.ShouldBe(AppointmentExpiredNotification.ForPatientTemplateKey);
        notification.TemplateKey.ShouldBe("appointment-expired-for-patient");
    }

    [Fact]
    public void Para_el_medico_el_correo_va_al_medico_con_su_plantilla()
    {
        var notification = Create(Data, AppointmentParticipant.Doctor);

        notification.Recipient.ShouldBe(new Recipient("medico@correo.com", "Luis Pérez"));
        notification.TemplateKey.ShouldBe(AppointmentExpiredNotification.ForDoctorTemplateKey);
        notification.TemplateKey.ShouldBe("appointment-expired-for-doctor");
    }

    [Fact]
    public void Un_argumento_invalido_es_un_error_de_programacion()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            AppointmentExpiredNotification.Create(Data, (AppointmentParticipant)99, DeadlineHours));
        Should.Throw<ArgumentNullException>(() =>
            AppointmentExpiredNotification.Create(null!, AppointmentParticipant.Patient, DeadlineHours));
    }

    [Fact]
    public void Un_rol_sin_plantilla_no_tiene_clave()
    {
        var notification = new AppointmentExpiredNotification(
            new Recipient("paciente@correo.com", "Ana Pérez"), Data, (AppointmentParticipant)99, DeadlineHours);

        Should.Throw<InvalidOperationException>(() => notification.TemplateKey);
    }

    [Fact]
    public void Las_plantillas_tienen_asunto()
    {
        _templates.Get(AppointmentExpiredNotification.ForPatientTemplateKey).Subject
            .ShouldBe("Su solicitud de cita venció");
        _templates.Get(AppointmentExpiredNotification.ForDoctorTemplateKey).Subject
            .ShouldBe("Una solicitud de cita venció");
    }

    [Fact]
    public void El_cuerpo_del_paciente_incluye_saludo_especialidad_medico_fecha_y_horas()
    {
        var body = Render(Create(Data, AppointmentParticipant.Patient));

        // El texto fijo de la plantilla va sin codificar; los valores sí se codifican en HTML.
        body.ShouldContain(WebUtility.HtmlEncode("Hola, Ana Pérez") + ":");
        body.ShouldContain("Su solicitud de cita de " + WebUtility.HtmlEncode("Cardiología") + " con el doctor "
            + WebUtility.HtmlEncode("Luis Pérez") + " se canceló automáticamente porque el médico no la confirmó a tiempo.");
        body.ShouldContain("05/01/2027");
        body.ShouldContain("09:00 a 11:00");
    }

    [Fact]
    public void El_cuerpo_del_medico_incluye_saludo_especialidad_paciente_plazo_fecha_y_horas()
    {
        var body = Render(Create(Data, AppointmentParticipant.Doctor));

        body.ShouldContain(WebUtility.HtmlEncode("Hola, Luis Pérez") + ":");
        body.ShouldContain("La solicitud de cita de " + WebUtility.HtmlEncode("Cardiología") + " de "
            + WebUtility.HtmlEncode("Ana Pérez")
            + " se canceló automáticamente porque no se confirmó antes de 48 horas de su inicio.");
        body.ShouldContain("05/01/2027");
        body.ShouldContain("09:00 a 11:00");
    }

    [Theory]
    [InlineData(AppointmentParticipant.Patient)]
    [InlineData(AppointmentParticipant.Doctor)]
    public void El_cuerpo_no_deja_variables_sin_reemplazar(AppointmentParticipant recipientRole)
    {
        TemplatePlaceholders.Extract(Render(Create(Data, recipientRole))).ShouldBeEmpty();
    }

    [Fact]
    public void El_cuerpo_codifica_en_HTML_los_nombres()
    {
        var body = Render(Create(Data with { PatientName = "<b>Ana</b> & Co" }, AppointmentParticipant.Patient));

        body.ShouldContain("&lt;b&gt;Ana&lt;/b&gt; &amp; Co");
        body.ShouldNotContain("<b>Ana</b>");
    }

    private static AppointmentExpiredNotification Create(
        AppointmentNotificationData data, AppointmentParticipant recipientRole) =>
        AppointmentExpiredNotification.Create(data, recipientRole, DeadlineHours);

    private string Render(AppointmentExpiredNotification notification)
    {
        var template = _templates.Get(notification.TemplateKey);

        return _renderer.Render(template.HtmlBody, notification.GetValues(), htmlEncode: true);
    }
}
