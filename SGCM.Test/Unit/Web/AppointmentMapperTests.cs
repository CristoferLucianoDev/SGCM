using SGCM.Data.Application.Scheduling.Commands.CancelAppointment;
using SGCM.Data.Application.Scheduling.Commands.CancelAppointmentByDoctor;
using SGCM.Data.Application.Scheduling.Commands.ExtendAppointment;
using SGCM.Data.Application.Scheduling.Commands.RejectAppointment;
using SGCM.Data.Application.Scheduling.Commands.RescheduleAppointmentByDoctor;
using SGCM.Data.Application.Scheduling.Commands.RequestAppointment;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorAssignment;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class AppointmentMapperTests
{
    private static readonly DateTimeOffset StartsAt = new(2027, 1, 12, 9, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void La_solicitud_de_asignacion_se_convierte_en_la_consulta_sin_cambiar_datos()
    {
        new DoctorAssignmentRequest(5, StartsAt, 3, 4).ToQuery()
            .ShouldBe(new GetDoctorAssignmentQuery(5, StartsAt, 3, 4));
    }

    [Fact]
    public void Sin_filtros_la_consulta_los_lleva_nulos()
    {
        new DoctorAssignmentRequest(5, StartsAt).ToQuery().ShouldBe(new GetDoctorAssignmentQuery(5, StartsAt));
    }

    [Fact]
    public void La_asignacion_se_convierte_en_la_respuesta_sin_cambiar_datos()
    {
        new DoctorAssignmentDto(7, "Luis Pérez", 1500m).ToResponse()
            .ShouldBe(new DoctorAssignmentResponse(7, "Luis Pérez", 1500m));
    }

    [Fact]
    public void Un_argumento_nulo_es_un_error_de_programacion()
    {
        Should.Throw<ArgumentNullException>(() => ((DoctorAssignmentRequest)null!).ToQuery());
        Should.Throw<ArgumentNullException>(() => ((DoctorAssignmentDto)null!).ToResponse());
    }

    [Fact]
    public void La_solicitud_de_cita_se_convierte_en_el_comando_sin_cambiar_datos()
    {
        new RequestAppointmentRequest(5, StartsAt, null, 7, 3, 4, "Control anual").ToCommand()
            .ShouldBe(new RequestAppointmentCommand(5, StartsAt, null, 7, 3, 4, "Control anual"));
    }

    [Fact]
    public void La_cita_creada_se_convierte_en_la_respuesta_sin_cambiar_datos()
    {
        var endsAt = StartsAt.AddHours(2);

        new RequestedAppointmentDto(99, 7, "Luis Pérez", 1500m, StartsAt, endsAt, true).ToResponse()
            .ShouldBe(new RequestAppointmentResponse(99, 7, "Luis Pérez", 1500m, StartsAt, endsAt, true));
    }

    [Fact]
    public void Un_argumento_nulo_de_la_solicitud_de_cita_es_un_error_de_programacion()
    {
        Should.Throw<ArgumentNullException>(() => ((RequestAppointmentRequest)null!).ToCommand());
        Should.Throw<ArgumentNullException>(() => ((RequestedAppointmentDto)null!).ToResponse());
        Should.Throw<ArgumentNullException>(() => ((CancelAppointmentRequest)null!).ToCommand(1));
        Should.Throw<ArgumentNullException>(() => ((CancelAppointmentByDoctorRequest)null!).ToCommand(1));
        Should.Throw<ArgumentNullException>(() => ((RejectAppointmentRequest)null!).ToCommand(1));
        Should.Throw<ArgumentNullException>(() => ((ExtendAppointmentRequest)null!).ToCommand(1));
        Should.Throw<ArgumentNullException>(() => ((RescheduleAppointmentByDoctorRequest)null!).ToCommand(1));
    }

    [Fact]
    public void La_solicitud_de_reprogramacion_del_medico_se_convierte_en_el_comando_con_el_id_de_la_ruta_y_la_hora()
    {
        new RescheduleAppointmentByDoctorRequest(StartsAt).ToCommand(99)
            .ShouldBe(new RescheduleAppointmentByDoctorCommand(99, StartsAt));
    }

    [Fact]
    public void La_solicitud_de_reprogramacion_del_medico_sin_hora_la_pasa_nula_para_que_la_valide_el_caso_de_uso()
    {
        new RescheduleAppointmentByDoctorRequest().ToCommand(99)
            .ShouldBe(new RescheduleAppointmentByDoctorCommand(99, null));
    }

    [Fact]
    public void La_solicitud_de_extension_se_convierte_en_el_comando_con_el_id_de_la_ruta_y_las_horas()
    {
        new ExtendAppointmentRequest(2).ToCommand(99).ShouldBe(new ExtendAppointmentCommand(99, 2));
    }

    [Fact]
    public void La_solicitud_de_rechazo_se_convierte_en_el_comando_con_el_id_de_la_ruta_y_el_motivo()
    {
        new RejectAppointmentRequest("Agenda completa").ToCommand(99)
            .ShouldBe(new RejectAppointmentCommand(99, "Agenda completa"));
    }

    [Fact]
    public void La_solicitud_de_rechazo_sin_motivo_lo_pasa_nulo_para_que_lo_valide_el_caso_de_uso()
    {
        new RejectAppointmentRequest().ToCommand(99).ShouldBe(new RejectAppointmentCommand(99, null));
    }

    [Fact]
    public void La_solicitud_de_cancelacion_del_medico_se_convierte_en_el_comando_con_el_id_de_la_ruta_y_el_motivo()
    {
        new CancelAppointmentByDoctorRequest("Emergencia familiar").ToCommand(99)
            .ShouldBe(new CancelAppointmentByDoctorCommand(99, "Emergencia familiar"));
    }

    [Fact]
    public void La_solicitud_de_cancelacion_se_convierte_en_el_comando_con_el_id_de_la_ruta_y_el_motivo()
    {
        new CancelAppointmentRequest("Tengo un viaje").ToCommand(99)
            .ShouldBe(new CancelAppointmentCommand(99, "Tengo un viaje"));
    }
}
