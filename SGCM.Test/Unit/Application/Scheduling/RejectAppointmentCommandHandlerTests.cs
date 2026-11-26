using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.RejectAppointment;
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

public sealed class RejectAppointmentCommandHandlerTests
{
    private const int UserId = 42;
    private const int AppointmentId = 99;
    private const int OtherDoctorId = 500;
    private const int HoursBeforeStart = 72;
    private const int HoursBeforeStartPastDeadline = 47;
    private const string Reason = "Agenda completa";

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly Appointment _appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
    private readonly RejectAppointmentCommandHandler _handler;

    public RejectAppointmentCommandHandlerTests()
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
        _handler = new RejectAppointmentCommandHandler(access, _clock, options);
    }

    [Fact]
    public async Task Rechaza_la_cita_propia_con_el_motivo_recortado_y_emite_el_evento()
    {
        var result = await Handle($"  {Reason}  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        _appointment.Status.ShouldBe(AppointmentStatus.Rejected);
        _appointment.StatusReason.ShouldBe(Reason);
        var domainEvent = _appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRejectedEvent>();
        domainEvent.Appointment.ShouldBeSameAs(_appointment);
    }

    [Fact]
    public async Task Sin_autenticar_se_rechaza_con_el_error_de_decision_sin_consultar_nada()
    {
        _currentUser.IsAuthenticated.Returns(false);

        AssertRejected(await Handle(Reason), AppointmentErrors.DecisionDoctorOnly);
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_se_rechaza()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        AssertRejected(await Handle(Reason), DoctorErrors.NotFound);
    }

    [Fact]
    public async Task Una_cita_inexistente_se_rechaza_como_no_encontrada()
    {
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((Appointment?)null);

        AssertRejected(await Handle(Reason), AppointmentErrors.NotFound);
    }

    [Fact]
    public async Task Una_cita_de_otro_medico_responde_igual_que_una_inexistente_y_no_cambia()
    {
        var foreign = AppointmentMother.InStatus(AppointmentStatus.Pending);
        typeof(Appointment).GetProperty(nameof(Appointment.DoctorId))!.SetValue(foreign, OtherDoctorId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(foreign);

        AssertRejected(await Handle(Reason), AppointmentErrors.NotFound);
        foreign.Status.ShouldBe(AppointmentStatus.Pending);
        foreign.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fuera_del_plazo_se_rechaza_y_la_cita_sigue_pendiente()
    {
        _clock.UtcNow = _appointment.StartsAt.AddHours(-HoursBeforeStartPastDeadline);

        AssertRejected(await Handle(Reason), AppointmentErrors.ConfirmationDeadlinePassed);
        _appointment.Status.ShouldBe(AppointmentStatus.Pending);
        _appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_cita_que_no_esta_pendiente_no_se_puede_rechazar()
    {
        var confirmed = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(confirmed);

        AssertRejected(await Handle(Reason), AppointmentErrors.NotPending);
        confirmed.Status.ShouldBe(AppointmentStatus.Confirmed);
    }

    [Fact]
    public async Task Sin_motivo_el_dominio_rechaza_y_la_cita_sigue_pendiente()
    {
        AssertRejected(await Handle(null), AppointmentErrors.StatusReasonRequired);
        _appointment.Status.ShouldBe(AppointmentStatus.Pending);
        _appointment.DomainEvents.ShouldBeEmpty();
    }

    private Task<Result<Unit>> Handle(string? reason) =>
        _handler.HandleAsync(new RejectAppointmentCommand(AppointmentId, reason), CancellationToken.None);

    private static void AssertRejected(Result<Unit> result, Error error)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }
}
