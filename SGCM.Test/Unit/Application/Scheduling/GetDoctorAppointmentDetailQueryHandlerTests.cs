using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorAppointmentDetail;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetDoctorAppointmentDetailQueryHandlerTests
{
    private const int UserId = 42;
    private const int DoctorId = 7;
    private const int AppointmentId = 90;
    private static readonly DateTimeOffset StartsAt = new(2027, 1, 12, 13, 0, 0, TimeSpan.Zero);

    private static readonly DoctorAppointmentDetailDto Detail = new(
        AppointmentId, 3, "María Pérez", "8095550100", "maria@correo.com", 5, "Cardiología",
        StartsAt, StartsAt.AddHours(2), new DateOnly(2027, 1, 12), new TimeOnly(9, 0), new TimeOnly(11, 0),
        AppointmentStatus.Pending, false, "Dolor de pecho");

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IDoctorScheduleReader _reader = Substitute.For<IDoctorScheduleReader>();

    public GetDoctorAppointmentDetailQueryHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(DoctorId);
        _reader.GetDetailAsync(DoctorId, AppointmentId, Arg.Any<CancellationToken>()).Returns(Detail);
    }

    [Fact]
    public async Task Devuelve_el_motivo_y_el_contacto_del_paciente_de_una_cita_propia()
    {
        var result = await Handle(AppointmentId);

        result.Value.ShouldBe(Detail);
        result.Value.ReasonForVisit.ShouldBe("Dolor de pecho");
        result.Value.PatientPhoneNumber.ShouldBe("8095550100");
        result.Value.PatientEmail.ShouldBe("maria@correo.com");
    }

    [Fact]
    public async Task Una_cita_de_otro_medico_o_inexistente_responde_cita_no_encontrada()
    {
        _reader.GetDetailAsync(DoctorId, 91, Arg.Any<CancellationToken>()).Returns((DoctorAppointmentDetailDto?)null);

        var result = await Handle(91);

        result.Error.ShouldBe(AppointmentErrors.NotFound);
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Un_identificador_invalido_se_rechaza_sin_leer()
    {
        (await Handle(0)).Error.Code.ShouldBe("Appointment.InvalidId");
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_usuario_que_no_es_medico_activo_no_ve_detalles()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        (await Handle(AppointmentId)).Error.ShouldBe(DoctorErrors.NotFound);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    private Task<Result<DoctorAppointmentDetailDto>> Handle(int appointmentId) =>
        new GetDoctorAppointmentDetailQueryHandler(_currentUser, _doctors, _reader, new GetDoctorAppointmentDetailQueryValidator())
            .HandleAsync(new GetDoctorAppointmentDetailQuery(appointmentId), CancellationToken.None);
}
