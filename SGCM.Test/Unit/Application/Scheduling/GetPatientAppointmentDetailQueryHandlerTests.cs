using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetPatientAppointmentDetailQueryHandlerTests
{
    private const int UserId = 42;
    private const int PatientId = 11;
    private const int AppointmentId = 7;
    private const int DoctorId = 3;
    private const int SpecialtyId = 5;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPatientRepository _patients = Substitute.For<IPatientRepository>();
    private readonly IPatientAppointmentHistoryReader _reader = Substitute.For<IPatientAppointmentHistoryReader>();

    public GetPatientAppointmentDetailQueryHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Patient).Returns(true);
        _currentUser.UserId.Returns(UserId);
        var patient = PatientMother.WithRequiredData(UserId);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(patient, PatientId);
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(patient);
    }

    [Fact]
    public async Task Devuelve_el_detalle_del_lector_con_el_paciente_autenticado_la_cita_y_el_token()
    {
        using var cancellation = new CancellationTokenSource();
        var detail = CreateDetail();
        _reader.GetAsync(PatientId, AppointmentId, cancellation.Token).Returns(detail);

        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentDetailQuery(AppointmentId), cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(detail);
        await _reader.Received(1).GetAsync(PatientId, AppointmentId, cancellation.Token);
    }

    [Fact]
    public async Task Una_consulta_invalida_devuelve_el_error_del_validador_sin_consultar_nada()
    {
        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentDetailQuery(0), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Appointment.InvalidAppointmentId");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        AssertNothingQueried();
    }

    [Fact]
    public async Task Sin_autenticar_se_rechaza_sin_consultar_nada()
    {
        _currentUser.IsAuthenticated.Returns(false);

        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentDetailQuery(AppointmentId), CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.HistoryPatientOnly);
        AssertNothingQueried();
    }

    [Fact]
    public async Task Con_otro_rol_se_rechaza_sin_consultar_nada()
    {
        _currentUser.IsInRole(UserRole.Patient).Returns(false);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);

        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentDetailQuery(AppointmentId), CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.HistoryPatientOnly);
        AssertNothingQueried();
    }

    [Fact]
    public async Task Sin_perfil_de_paciente_se_rechaza_sin_llamar_al_lector()
    {
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Patient?)null);

        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentDetailQuery(AppointmentId), CancellationToken.None);

        result.Error.ShouldBe(PatientErrors.ProfileNotFound);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Si_el_lector_no_devuelve_la_cita_responde_que_no_existe()
    {
        _reader.GetAsync(PatientId, AppointmentId, Arg.Any<CancellationToken>())
            .Returns((PatientAppointmentDetailDto?)null);

        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentDetailQuery(AppointmentId), CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.NotFound);
    }

    private static PatientAppointmentDetailDto CreateDetail()
    {
        var startsAt = new DateTimeOffset(2027, 1, 12, 13, 0, 0, TimeSpan.Zero);
        var appointment = new PatientAppointmentDto(
            AppointmentId,
            startsAt,
            startsAt.AddHours(AppointmentMother.DurationHours),
            new DateOnly(2027, 1, 12),
            new TimeOnly(9, 0),
            new TimeOnly(11, 0),
            DoctorId,
            "Luis Pérez",
            SpecialtyId,
            "Cardiología",
            AppointmentStatus.Pending);

        return new PatientAppointmentDetailDto(appointment, "Dolor de cabeza", null);
    }

    private void AssertNothingQueried()
    {
        _patients.ReceivedCalls().ShouldBeEmpty();
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    private GetPatientAppointmentDetailQueryHandler CreateHandler() =>
        new(_currentUser, _patients, _reader, new GetPatientAppointmentDetailQueryValidator());
}
