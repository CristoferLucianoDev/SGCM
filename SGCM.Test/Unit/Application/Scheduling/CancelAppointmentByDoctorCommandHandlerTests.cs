using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.CancelAppointmentByDoctor;
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

public sealed class CancelAppointmentByDoctorCommandHandlerTests
{
    private const int UserId = 42;
    private const int AppointmentId = 99;
    private const int OtherDoctorId = 500;
    private const int HoursBeforeStartInsideShortNotice = 5;
    private const int HoursBeforeStartInsidePatientDeadline = 23;
    private const string Reason = "Emergencia familiar";

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2027, 1, 4, 16, 0, 0, TimeSpan.Zero) };
    private readonly Appointment _appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
    private readonly CancelAppointmentByDoctorCommandHandler _handler;

    public CancelAppointmentByDoctorCommandHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((int?)AppointmentMother.DoctorId);

        SetId(_appointment, AppointmentId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(_appointment);

        var options = Options.Create(new SchedulingOptions
        {
            PatientCancellationDeadlineHours = AppointmentMother.PatientCancellationDeadlineHours,
            DoctorLateCancellationThresholdHours = AppointmentMother.ShortNoticeThresholdHours,
            DoctorLateCancellationMinimumWords = AppointmentMother.ShortNoticeMinimumWords
        });
        var access = new DoctorAppointmentAccess(_currentUser, _doctors, _appointments);
        _handler = new CancelAppointmentByDoctorCommandHandler(access, _clock, options);
    }

    [Fact]
    public async Task Cancela_la_cita_propia_con_el_motivo_y_emite_el_evento_del_medico()
    {
        var result = await Handle(Reason);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        _appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
        _appointment.StatusReason.ShouldBe(Reason);
        var domainEvent = _appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentCancelledEvent>();
        domainEvent.Appointment.ShouldBeSameAs(_appointment);
        domainEvent.CancelledBy.ShouldBe(AppointmentCanceller.Doctor);
    }

    [Fact]
    public async Task Sin_autenticar_se_rechaza_sin_consultar_nada()
    {
        _currentUser.IsAuthenticated.Returns(false);

        AssertRejected(await Handle(Reason), AppointmentErrors.CancellationDoctorOnly);
        AssertNothingQueried();
    }

    [Fact]
    public async Task Sin_el_rol_de_medico_se_rechaza_sin_consultar_nada()
    {
        _currentUser.IsInRole(UserRole.Doctor).Returns(false);

        AssertRejected(await Handle(Reason), AppointmentErrors.CancellationDoctorOnly);
        AssertNothingQueried();
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
    public async Task Con_poco_aviso_un_motivo_corto_se_rechaza_y_la_cita_sigue_pendiente()
    {
        _clock.UtcNow = _appointment.StartsAt.AddHours(-HoursBeforeStartInsideShortNotice);

        var result = await Handle(Reason);

        AssertRejected(result, AppointmentErrors.ShortNoticeReasonTooShort(AppointmentMother.ShortNoticeMinimumWords));
        _appointment.Status.ShouldBe(AppointmentStatus.Pending);
    }

    [Fact]
    public async Task Con_poco_aviso_un_motivo_con_las_palabras_minimas_cancela()
    {
        _clock.UtcNow = _appointment.StartsAt.AddHours(-HoursBeforeStartInsideShortNotice);

        var result = await Handle(string.Join(" ", Enumerable.Repeat("palabra", AppointmentMother.ShortNoticeMinimumWords)));

        result.IsSuccess.ShouldBeTrue();
        _appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task A_menos_del_plazo_del_paciente_cancela_igual()
    {
        _clock.UtcNow = _appointment.StartsAt.AddHours(-HoursBeforeStartInsidePatientDeadline);

        var result = await Handle(Reason);

        result.IsSuccess.ShouldBeTrue();
        _appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Una_cita_completada_no_se_puede_cancelar()
    {
        var completed = AppointmentMother.InStatus(AppointmentStatus.Completed);
        SetId(completed, AppointmentId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(completed);

        AssertRejected(await Handle(Reason), AppointmentErrors.InvalidTransition);
        completed.Status.ShouldBe(AppointmentStatus.Completed);
    }

    private static void SetId(Appointment appointment, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(appointment, id);

    private Task<Result<Unit>> Handle(string? reason) =>
        _handler.HandleAsync(new CancelAppointmentByDoctorCommand(AppointmentId, reason), CancellationToken.None);

    private void AssertNothingQueried()
    {
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    private static void AssertRejected(Result<Unit> result, Error error)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }
}
