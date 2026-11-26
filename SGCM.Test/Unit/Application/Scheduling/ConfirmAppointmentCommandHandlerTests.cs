using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.ConfirmAppointment;
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

public sealed class ConfirmAppointmentCommandHandlerTests
{
    private const int UserId = 42;
    private const int AppointmentId = 99;
    private const int OtherDoctorId = 500;
    private const int HoursBeforeStart = 72;
    private const int HoursBeforeStartPastDeadline = 47;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly Appointment _appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
    private readonly ConfirmAppointmentCommandHandler _handler;

    public ConfirmAppointmentCommandHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((int?)AppointmentMother.DoctorId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(_appointment);
        _clock.UtcNow = _appointment.StartsAt.AddHours(-HoursBeforeStart);

        var options = Options.Create(new SchedulingOptions
        {
            ConfirmationDeadlineHours = AppointmentMother.ConfirmationDeadlineHours
        });
        var access = new DoctorAppointmentAccess(_currentUser, _doctors, _appointments);
        _handler = new ConfirmAppointmentCommandHandler(access, _clock, options);
    }

    [Fact]
    public async Task Confirma_la_cita_propia_y_emite_el_evento()
    {
        var result = await Handle();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        _appointment.Status.ShouldBe(AppointmentStatus.Confirmed);
        var domainEvent = _appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentConfirmedEvent>();
        domainEvent.Appointment.ShouldBeSameAs(_appointment);
    }

    [Fact]
    public async Task Sin_autenticar_se_rechaza_con_el_error_de_decision_sin_consultar_nada()
    {
        _currentUser.IsAuthenticated.Returns(false);

        AssertRejected(await Handle(), AppointmentErrors.DecisionDoctorOnly);
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
        var foreign = AppointmentMother.InStatus(AppointmentStatus.Pending);
        typeof(Appointment).GetProperty(nameof(Appointment.DoctorId))!.SetValue(foreign, OtherDoctorId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(foreign);

        AssertRejected(await Handle(), AppointmentErrors.NotFound);
        foreign.Status.ShouldBe(AppointmentStatus.Pending);
        foreign.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fuera_del_plazo_se_rechaza_y_la_cita_sigue_pendiente()
    {
        _clock.UtcNow = _appointment.StartsAt.AddHours(-HoursBeforeStartPastDeadline);

        AssertRejected(await Handle(), AppointmentErrors.ConfirmationDeadlinePassed);
        _appointment.Status.ShouldBe(AppointmentStatus.Pending);
        _appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_cita_que_no_esta_pendiente_no_se_puede_confirmar()
    {
        var confirmed = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(confirmed);

        AssertRejected(await Handle(), AppointmentErrors.NotPending);
        confirmed.Status.ShouldBe(AppointmentStatus.Confirmed);
    }

    private Task<Result<Unit>> Handle() =>
        _handler.HandleAsync(new ConfirmAppointmentCommand(AppointmentId), CancellationToken.None);

    private static void AssertRejected(Result<Unit> result, Error error)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }
}
