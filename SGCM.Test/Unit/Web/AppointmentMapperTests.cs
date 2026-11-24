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
    }
}
