using NSubstitute;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Messaging;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Infrastructure.Notifications;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Messaging;

public sealed class ReminderComposerTests
{
    private static readonly AppointmentNotificationData Appointment = new(
        AppointmentId: 10,
        PatientName: "Ana Pérez",
        PatientEmail: "ana@correo.com",
        DoctorName: "Luis Gómez",
        DoctorEmail: "luis@correo.com",
        SpecialtyName: "Cardiología",
        ConsultationFee: 2500m,
        AppointmentDate: new DateOnly(2027, 1, 12),
        StartTime: new TimeOnly(9, 0),
        EndTime: new TimeOnly(11, 0),
        ReasonForVisit: null);

    [Fact]
    public void Reemplaza_las_variables_del_asunto_y_del_texto_con_los_datos_de_la_cita()
    {
        var composed = new ReminderComposer(new TemplateRenderer()).Compose(ReminderTemplateMother.Global(), Appointment);

        composed.ShouldBe(new ComposedReminder(
            "Recordatorio: su cita de Cardiología es manana",
            "Hola Ana Pérez, le recordamos su cita de Cardiología con Luis Gómez el 12/01/2027 a las 09:00."));
    }

    [Fact]
    public void Deja_el_texto_sin_codificar_como_HTML_para_guardarlo_y_editarlo()
    {
        var appointment = Appointment with { PatientName = "Ana <Pérez> & Co" };

        var composed = new ReminderComposer(new TemplateRenderer())
            .Compose(ReminderTemplateMother.Global(body: "Hola {PatientName}"), appointment);

        composed.Message.ShouldBe("Hola Ana <Pérez> & Co");
    }

    [Fact]
    public void Usa_el_renderizador_sin_codificar_con_las_cinco_variables()
    {
        var renderer = Substitute.For<ITemplateRenderer>();
        renderer.Render(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<bool>())
            .Returns(call => call.ArgAt<string>(0).ToUpperInvariant());

        var composed = new ReminderComposer(renderer).Compose(ReminderTemplateMother.Global("Asunto", "Texto"), Appointment);

        composed.ShouldBe(new ComposedReminder("ASUNTO", "TEXTO"));
        renderer.Received(2).Render(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(values => values.Keys.Order().SequenceEqual(AppointmentTemplateValues.Names.Order())),
            false);
    }

    [Fact]
    public void Las_variables_son_las_que_acepta_la_base_con_la_fecha_y_la_hora_de_los_correos()
    {
        AppointmentTemplateValues.From(Appointment).ShouldBe(new Dictionary<string, string>
        {
            ["PatientName"] = "Ana Pérez",
            ["DoctorName"] = "Luis Gómez",
            ["AppointmentDate"] = "12/01/2027",
            ["StartTime"] = "09:00",
            ["SpecialtyName"] = "Cardiología"
        }, ignoreOrder: true);
    }
}
