using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Fakes;
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
    private const int HoursBeforeStart = 72;
    private const int ExpectedHoursLeftToConfirm = HoursBeforeStart - AppointmentMother.ConfirmationDeadlineHours;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPatientRepository _patients = Substitute.For<IPatientRepository>();
    private readonly IPatientAppointmentHistoryReader _reader = Substitute.For<IPatientAppointmentHistoryReader>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly Appointment _appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

    private static readonly DoctorPublicProfileDto DoctorProfile = new(
        DoctorId, "Luis Pérez", [new(SpecialtyId, "Cardiología")], new(1, "Distrito Nacional"),
        new(2, "Santo Domingo de Guzmán"), "Av. Abraham Lincoln 100", 2500m, 12, "Cardiólogo.");

    public GetPatientAppointmentDetailQueryHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Patient).Returns(true);
        _currentUser.UserId.Returns(UserId);
        var patient = PatientMother.WithRequiredData(UserId);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(patient, PatientId);
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(patient);
        _appointments.GetByIdAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(_appointment);
        _clock.UtcNow = _appointment.StartsAt.AddHours(-HoursBeforeStart);
    }

    [Fact]
    public async Task Devuelve_el_detalle_del_lector_con_el_paciente_autenticado_la_cita_y_el_token()
    {
        using var cancellation = new CancellationTokenSource();
        var data = CreateData();
        _reader.GetAsync(PatientId, AppointmentId, cancellation.Token).Returns(data);

        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentDetailQuery(AppointmentId), cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Appointment.ShouldBeSameAs(data.Appointment);
        result.Value.ReasonForVisit.ShouldBe(data.ReasonForVisit);
        result.Value.StatusReason.ShouldBe(data.StatusReason);
        result.Value.DoctorProfile.ShouldBeSameAs(DoctorProfile);
        await _reader.Received(1).GetAsync(PatientId, AppointmentId, cancellation.Token);
        await _appointments.Received(1).GetByIdAsync(AppointmentId, cancellation.Token);
    }

    [Fact]
    public async Task Con_un_medico_desactivado_muestra_su_nombre_y_la_especialidad_sin_perfil_y_sin_error()
    {
        _reader.GetAsync(PatientId, AppointmentId, Arg.Any<CancellationToken>())
            .Returns(CreateData() with { DoctorProfile = null });

        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentDetailQuery(AppointmentId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DoctorProfile.ShouldBeNull();
        result.Value.Appointment.DoctorName.ShouldBe("Luis Pérez");
        result.Value.Appointment.SpecialtyName.ShouldBe("Cardiología");
    }

    [Fact]
    public async Task Las_acciones_calculadas_por_la_cita_llegan_al_resultado()
    {
        _reader.GetAsync(PatientId, AppointmentId, Arg.Any<CancellationToken>()).Returns(CreateData());

        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentDetailQuery(AppointmentId), CancellationToken.None);

        var actions = result.Value.AllowedActions;
        actions.Actions.ShouldBe(new[]
        {
            AppointmentAction.Cancel, AppointmentAction.Reschedule, AppointmentAction.ChangeDoctor
        });
        actions.RequiresExtendedCancellationReason.ShouldBeFalse();
        actions.ConfirmationDeadline.ShouldBe(_appointment.StartsAt.AddHours(-AppointmentMother.ConfirmationDeadlineHours));
        actions.TimeUntilConfirmationDeadline.ShouldBe(TimeSpan.FromHours(ExpectedHoursLeftToConfirm));
    }

    [Fact]
    public async Task Si_la_cita_desaparece_entre_las_dos_lecturas_responde_que_no_existe()
    {
        _reader.GetAsync(PatientId, AppointmentId, Arg.Any<CancellationToken>()).Returns(CreateData());
        _appointments.GetByIdAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((Appointment?)null);

        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentDetailQuery(AppointmentId), CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.NotFound);
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
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Si_el_lector_no_devuelve_la_cita_responde_que_no_existe()
    {
        _reader.GetAsync(PatientId, AppointmentId, Arg.Any<CancellationToken>())
            .Returns((PatientAppointmentDetailData?)null);

        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentDetailQuery(AppointmentId), CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.NotFound);
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    private static PatientAppointmentDetailData CreateData()
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

        return new PatientAppointmentDetailData(appointment, "Dolor de cabeza", null, DoctorProfile);
    }

    private void AssertNothingQueried()
    {
        _patients.ReceivedCalls().ShouldBeEmpty();
        _reader.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    private GetPatientAppointmentDetailQueryHandler CreateHandler() =>
        new(_currentUser, _patients, _reader, _appointments, _clock, CreateOptions(),
            new GetPatientAppointmentDetailQueryValidator());

    private static IOptions<SchedulingOptions> CreateOptions() => Options.Create(new SchedulingOptions
    {
        PatientCancellationDeadlineHours = AppointmentMother.PatientCancellationDeadlineHours,
        PatientRescheduleDeadlineHours = AppointmentMother.PatientRescheduleDeadlineHours,
        ConfirmationDeadlineHours = AppointmentMother.ConfirmationDeadlineHours,
        DoctorLateCancellationThresholdHours = AppointmentMother.ShortNoticeThresholdHours,
        DoctorLateCancellationMinimumWords = AppointmentMother.ShortNoticeMinimumWords
    });
}
