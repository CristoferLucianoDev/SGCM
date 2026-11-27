using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.CompleteAppointment;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class CompleteAppointmentCommandHandlerTests
{
    private const int UserId = 42;
    private const int AppointmentId = 99;
    private const int OtherDoctorId = 500;
    private const int HoursIntoTheAppointment = 1;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly Appointment _appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
    private readonly CompleteAppointmentCommandHandler _handler;

    public CompleteAppointmentCommandHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((int?)AppointmentMother.DoctorId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(_appointment);
        _clock.UtcNow = _appointment.StartsAt.AddHours(HoursIntoTheAppointment);

        var access = new DoctorAppointmentAccess(_currentUser, _doctors, _appointments);
        _handler = new CompleteAppointmentCommandHandler(access, _clock);
    }

    [Fact]
    public async Task Termina_la_cita_en_curso_con_el_reloj_como_fin_y_emite_el_evento()
    {
        var result = await Handle();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        _appointment.Status.ShouldBe(AppointmentStatus.Completed);
        _appointment.EndsAt.ShouldBe(_clock.UtcNow);
        var domainEvent = _appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentCompletedEvent>();
        domainEvent.Appointment.ShouldBeSameAs(_appointment);
    }

    [Fact]
    public async Task Una_cita_que_no_esta_en_curso_se_rechaza_y_no_cambia()
    {
        var endsAt = _appointment.EndsAt;
        _clock.UtcNow = _appointment.StartsAt.AddSeconds(-1);

        AssertRejected(await Handle(), AppointmentErrors.NotInProgress);
        _appointment.Status.ShouldBe(AppointmentStatus.Confirmed);
        _appointment.EndsAt.ShouldBe(endsAt);
        _appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sin_autenticar_se_rechaza_con_el_error_de_progreso_sin_consultar_nada()
    {
        _currentUser.IsAuthenticated.Returns(false);

        AssertRejected(await Handle(), AppointmentErrors.ProgressDoctorOnly);
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_se_rechaza()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        AssertRejected(await Handle(), DoctorErrors.NotFound);
    }

    [Fact]
    public async Task Una_cita_inexistente_se_rechaza_como_no_encontrada()
    {
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((Appointment?)null);

        AssertRejected(await Handle(), AppointmentErrors.NotFound);
    }

    [Fact]
    public async Task Una_cita_de_otro_medico_responde_igual_que_una_inexistente_y_no_cambia()
    {
        var foreign = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        typeof(Appointment).GetProperty(nameof(Appointment.DoctorId))!.SetValue(foreign, OtherDoctorId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(foreign);

        AssertRejected(await Handle(), AppointmentErrors.NotFound);
        foreign.Status.ShouldBe(AppointmentStatus.Confirmed);
        foreign.DomainEvents.ShouldBeEmpty();
    }

    private Task<Result<Unit>> Handle() =>
        _handler.HandleAsync(new CompleteAppointmentCommand(AppointmentId), CancellationToken.None);

    private static void AssertRejected(Result<Unit> result, Error error)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }
}
