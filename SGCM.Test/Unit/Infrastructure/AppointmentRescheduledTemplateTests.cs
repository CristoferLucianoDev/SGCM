using System.Net;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

/// <summary>
/// Pruebas de la notificación de cita reprogramada y de sus dos plantillas reales.
/// </summary>
public sealed class AppointmentRescheduledTemplateTests
{
    private const int UndefinedParticipant = 99;

    private static readonly DateOnly PreviousDate = new(2027, 1, 4);
    private static readonly TimeOnly PreviousStartTime = new(8, 0);

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
    public void Los_valores_llevan_las_fechas_como_dd_MM_yyyy_y_las_horas_en_24_horas_tanto_nuevas_como_anteriores()
    {
        var values = Create(Data with { StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(16, 0) }).GetValues();

        values["AppointmentDate"].ShouldBe("05/01/2027");
        values["StartTime"].ShouldBe("14:00");
        values["EndTime"].ShouldBe("16:00");
        values["PreviousDate"].ShouldBe("04/01/2027");
        values["PreviousStartTime"].ShouldBe("08:00");
        values["DoctorName"].ShouldBe("Luis Pérez");
        values["PatientName"].ShouldBe("Ana Pérez");
        values["SpecialtyName"].ShouldBe("Cardiología");
    }

    [Fact]
    public void Si_reprograma_el_paciente_el_correo_va_al_medico_con_la_plantilla_del_paciente()
    {
        var notification = Create(Data);

        notification.Recipient.ShouldBe(new Recipient("medico@correo.com", "Luis Pérez"));
        notification.TemplateKey.ShouldBe(AppointmentRescheduledNotification.RescheduledByPatientTemplateKey);
        notification.TemplateKey.ShouldBe("appointment-rescheduled-by-patient");
        notification.RescheduledBy.ShouldBe(AppointmentParticipant.Patient);
        notification.PreviousDate.ShouldBe(PreviousDate);
        notification.PreviousStartTime.ShouldBe(PreviousStartTime);
    }

    [Fact]
    public void Si_reprograma_el_medico_el_correo_va_al_paciente_con_la_plantilla_del_medico()
    {
        var notification = CreateByDoctor(Data);

        notification.Recipient.ShouldBe(new Recipient("paciente@correo.com", "Ana Pérez"));
        notification.TemplateKey.ShouldBe(AppointmentRescheduledNotification.RescheduledByDoctorTemplateKey);
        notification.TemplateKey.ShouldBe("appointment-rescheduled-by-doctor");
        notification.RescheduledBy.ShouldBe(AppointmentParticipant.Doctor);
    }

    [Fact]
    public void Un_argumento_invalido_es_un_error_de_programacion()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => AppointmentRescheduledNotification.Create(
            Data, (AppointmentParticipant)UndefinedParticipant, PreviousDate, PreviousStartTime));
        Should.Throw<ArgumentNullException>(() => AppointmentRescheduledNotification.Create(
            null!, AppointmentParticipant.Patient, PreviousDate, PreviousStartTime));
    }

    [Fact]
    public void Una_notificacion_construida_con_un_participante_no_definido_no_tiene_plantilla()
    {
        var notification = new AppointmentRescheduledNotification(
            new Recipient("alguien@correo.com", "Alguien"),
            Data,
            (AppointmentParticipant)UndefinedParticipant,
            PreviousDate,
            PreviousStartTime);

        Should.Throw<InvalidOperationException>(() => notification.TemplateKey);
    }

    [Fact]
    public void La_plantilla_del_paciente_tiene_asunto()
    {
        _templates.Get(AppointmentRescheduledNotification.RescheduledByPatientTemplateKey).Subject
            .ShouldBe("Cita reprogramada por el paciente");
    }

    [Fact]
    public void La_plantilla_del_medico_tiene_asunto()
    {
        _templates.Get(AppointmentRescheduledNotification.RescheduledByDoctorTemplateKey).Subject
            .ShouldBe("Su cita fue reprogramada");
    }

    [Fact]
    public void El_cuerpo_del_paciente_incluye_saludo_nombres_especialidad_y_los_dos_horarios()
    {
        var body = Render(Create(Data));

        // El texto fijo de la plantilla va sin codificar; los valores sí se codifican en HTML.
        body.ShouldContain(WebUtility.HtmlEncode("Hola, Luis Pérez") + ":");
        body.ShouldContain(WebUtility.HtmlEncode("Ana Pérez") + " reprogramó su cita de " + WebUtility.HtmlEncode("Cardiología"));
        body.ShouldContain("Horario anterior:</strong> 04/01/2027 a las 08:00");
        body.ShouldContain("Nuevo horario:</strong> 05/01/2027, de 09:00 a 11:00");
        body.ShouldContain("La cita volvió a quedar pendiente. Confírmala o recházala desde tu agenda.");
    }

    [Fact]
    public void El_cuerpo_del_medico_incluye_saludo_nombres_especialidad_y_los_dos_horarios()
    {
        var body = Render(CreateByDoctor(Data));

        body.ShouldContain(WebUtility.HtmlEncode("Hola, Ana Pérez") + ":");
        body.ShouldContain("El doctor " + WebUtility.HtmlEncode("Luis Pérez") + " cambió el horario de su cita de "
            + WebUtility.HtmlEncode("Cardiología"));
        body.ShouldContain("Horario anterior:</strong> 04/01/2027 a las 08:00");
        body.ShouldContain("Nuevo horario:</strong> 05/01/2027, de 09:00 a 11:00");
        body.ShouldContain("La cita queda confirmada en el nuevo horario.");
        body.ShouldContain(
            "Si el nuevo horario no le conviene, puede cancelar o reprogramar la cita desde el Sistema de Gestión de "
            + "Citas Médicas dentro de los plazos permitidos.");
    }

    [Fact]
    public void Los_cuerpos_no_dejan_variables_sin_reemplazar()
    {
        TemplatePlaceholders.Extract(Render(Create(Data))).ShouldBeEmpty();
        TemplatePlaceholders.Extract(Render(CreateByDoctor(Data))).ShouldBeEmpty();
    }

    [Fact]
    public void Los_cuerpos_codifican_en_HTML_los_nombres()
    {
        var hostile = Data with { PatientName = "<b>Ana</b> & Co", DoctorName = "<i>Luis</i>" };

        foreach (var body in new[] { Render(Create(hostile)), Render(CreateByDoctor(hostile)) })
        {
            body.ShouldContain("&lt;b&gt;Ana&lt;/b&gt; &amp; Co");
            body.ShouldContain("&lt;i&gt;Luis&lt;/i&gt;");
            body.ShouldNotContain("<b>Ana</b>");
            body.ShouldNotContain("<i>Luis</i>");
        }
    }

    private static AppointmentRescheduledNotification Create(AppointmentNotificationData data) =>
        AppointmentRescheduledNotification.Create(data, AppointmentParticipant.Patient, PreviousDate, PreviousStartTime);

    private static AppointmentRescheduledNotification CreateByDoctor(AppointmentNotificationData data) =>
        AppointmentRescheduledNotification.Create(data, AppointmentParticipant.Doctor, PreviousDate, PreviousStartTime);

    private string Render(AppointmentRescheduledNotification notification)
    {
        var template = _templates.Get(notification.TemplateKey);

        return _renderer.Render(template.HtmlBody, notification.GetValues(), htmlEncode: true);
    }
}
