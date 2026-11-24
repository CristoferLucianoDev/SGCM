using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.CancelAppointment;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class CancelAppointmentCommandHandlerTests
{
    private const int UserId = 42;
    private const int AppointmentId = 99;
    private const int OtherPatientId = 500;
    private const int HoursBeforeStartPastDeadline = 23;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPatientRepository _patients = Substitute.For<IPatientRepository>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2027, 1, 4, 16, 0, 0, TimeSpan.Zero) };
    private readonly Appointment _appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
    private readonly CancelAppointmentCommandHandler _handler;

    public CancelAppointmentCommandHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Patient).Returns(true);
        _currentUser.UserId.Returns(UserId);
        var patient = PatientMother.WithRequiredData(UserId);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(patient, AppointmentMother.PatientId);
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(patient);

        SetId(_appointment, AppointmentId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(_appointment);

        var options = Options.Create(new SchedulingOptions
        {
            PatientCancellationDeadlineHours = AppointmentMother.PatientCancellationDeadlineHours,
            DoctorLateCancellationThresholdHours = AppointmentMother.ShortNoticeThresholdHours,
            DoctorLateCancellationMinimumWords = AppointmentMother.ShortNoticeMinimumWords
        });
        _handler = new CancelAppointmentCommandHandler(_currentUser, _patients, _appointments, _clock, options);
    }

    [Fact]
    public async Task Cancela_la_cita_propia_con_el_motivo_recortado_y_emite_el_evento()
    {
        var result = await Handle("  Tengo un viaje  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        _appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
        _appointment.StatusReason.ShouldBe("Tengo un viaje");
        var domainEvent = _appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentCancelledEvent>();
        domainEvent.Appointment.ShouldBeSameAs(_appointment);
        domainEvent.CancelledBy.ShouldBe(AppointmentCanceller.Patient);
    }

    [Fact]
    public async Task Sin_autenticar_se_rechaza_sin_consultar_repositorios()
    {
        _currentUser.IsAuthenticated.Returns(false);

        AssertRejected(await Handle(null), AppointmentErrors.CancellationPatientOnly);
        AssertNoRepositoryCalls();
    }

    [Fact]
    public async Task Sin_el_rol_de_paciente_se_rechaza_sin_consultar_repositorios()
    {
        _currentUser.IsInRole(UserRole.Patient).Returns(false);

        AssertRejected(await Handle(null), AppointmentErrors.CancellationPatientOnly);
        AssertNoRepositoryCalls();
    }

    [Fact]
    public async Task Sin_perfil_de_paciente_se_rechaza()
    {
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Patient?)null);

        AssertRejected(await Handle(null), PatientErrors.ProfileNotFound);
    }

    [Fact]
    public async Task Una_cita_inexistente_se_rechaza_como_no_encontrada()
    {
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((Appointment?)null);

        AssertRejected(await Handle(null), AppointmentErrors.NotFound);
    }

    [Fact]
    public async Task Una_cita_de_otro_paciente_responde_igual_que_una_inexistente_y_no_cambia()
    {
        var foreign = AppointmentMother.InStatus(AppointmentStatus.Pending);
        typeof(Appointment).GetProperty(nameof(Appointment.PatientId))!.SetValue(foreign, OtherPatientId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(foreign);

        AssertRejected(await Handle(null), AppointmentErrors.NotFound);
        foreign.Status.ShouldBe(AppointmentStatus.Pending);
        foreign.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fuera_de_plazo_se_rechaza_y_la_cita_sigue_pendiente()
    {
        _clock.UtcNow = _appointment.StartsAt.AddHours(-HoursBeforeStartPastDeadline);

        var result = await Handle(null);

        AssertRejected(result, AppointmentErrors.CancellationDeadlinePassed);
        result.Error.Description.ShouldContain("llamar al médico");
        _appointment.Status.ShouldBe(AppointmentStatus.Pending);
    }

    [Fact]
    public async Task Una_cita_completada_no_se_puede_cancelar()
    {
        var completed = AppointmentMother.InStatus(AppointmentStatus.Completed);
        SetId(completed, AppointmentId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(completed);

        AssertRejected(await Handle(null), AppointmentErrors.InvalidTransition);
        completed.Status.ShouldBe(AppointmentStatus.Completed);
    }

    private static void SetId(Appointment appointment, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(appointment, id);

    private Task<Result<Unit>> Handle(string? reason) =>
        _handler.HandleAsync(new CancelAppointmentCommand(AppointmentId, reason), CancellationToken.None);

    private void AssertNoRepositoryCalls()
    {
        _patients.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    private static void AssertRejected(Result<Unit> result, Error error)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }
}
