using System.Net;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Infrastructure.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

/// <summary>
/// Pruebas de la notificación de cita confirmada y de su plantilla real.
/// </summary>
public sealed class AppointmentConfirmedTemplateTests
{
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
        "Control anual",
        "Calle El Conde 12, Santo Domingo de Guzmán, Distrito Nacional");

    private readonly NotificationTemplateStore _templates = new();
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void Los_valores_llevan_la_fecha_como_dd_MM_yyyy_las_horas_en_24_horas_y_la_tarifa_con_dos_decimales()
    {
        var values = Create(Data with { StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(16, 0) }).GetValues();

        values["AppointmentDate"].ShouldBe("05/01/2027");
        values["StartTime"].ShouldBe("14:00");
        values["EndTime"].ShouldBe("16:00");
        values["ConsultationFee"].ShouldBe("1,500.00");
        values["PatientName"].ShouldBe("Ana Pérez");
        values["DoctorName"].ShouldBe("Luis Pérez");
        values["SpecialtyName"].ShouldBe("Cardiología");
        values["DoctorAddress"].ShouldBe("Calle El Conde 12, Santo Domingo de Guzmán, Distrito Nacional");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Sin_direccion_conocida_indica_consultarla_en_el_perfil_del_medico(string? address)
    {
        Create(Data with { DoctorAddress = address }).GetValues()["DoctorAddress"]
            .ShouldBe(AppointmentConfirmedNotification.NoAddress);
    }

    [Fact]
    public void La_notificacion_usa_la_clave_de_la_plantilla_de_confirmacion()
    {
        var notification = Create(Data);

        notification.TemplateKey.ShouldBe(AppointmentConfirmedNotification.ConfirmedTemplateKey);
        notification.TemplateKey.ShouldBe("appointment-confirmed");
        notification.Recipient.ShouldBe(new Recipient("paciente@correo.com", "Ana Pérez"));
    }

    [Fact]
    public void La_plantilla_tiene_asunto()
    {
        _templates.Get(AppointmentConfirmedNotification.ConfirmedTemplateKey).Subject.ShouldBe("Su cita fue confirmada");
    }

    [Fact]
    public void El_cuerpo_incluye_saludo_medico_especialidad_fecha_horas_direccion_y_tarifa()
    {
        var body = Render(Create(Data));

        // El texto fijo de la plantilla va sin codificar; los valores sí se codifican en HTML.
        body.ShouldContain(WebUtility.HtmlEncode("Hola, Ana Pérez") + ":");
        body.ShouldContain("El doctor " + WebUtility.HtmlEncode("Luis Pérez") + " confirmó su cita de "
            + WebUtility.HtmlEncode("Cardiología") + ".");
        body.ShouldContain("05/01/2027");
        body.ShouldContain("09:00 a 11:00");
        body.ShouldContain("RD$ 1,500.00");
        body.ShouldContain("Médico:</strong> " + WebUtility.HtmlEncode("Luis Pérez"));
        body.ShouldContain("Especialidad:</strong> " + WebUtility.HtmlEncode("Cardiología"));
        body.ShouldContain("Dirección:</strong> "
            + WebUtility.HtmlEncode("Calle El Conde 12, Santo Domingo de Guzmán, Distrito Nacional"));
    }

    [Fact]
    public void El_cuerpo_no_deja_variables_sin_reemplazar()
    {
        TemplatePlaceholders.Extract(Render(Create(Data))).ShouldBeEmpty();
    }

    private static AppointmentConfirmedNotification Create(AppointmentNotificationData data) =>
        new(new Recipient(data.PatientEmail, data.PatientName), data);

    private string Render(AppointmentConfirmedNotification notification)
    {
        var template = _templates.Get(notification.TemplateKey);

        return _renderer.Render(template.HtmlBody, notification.GetValues(), htmlEncode: true);
    }
}
