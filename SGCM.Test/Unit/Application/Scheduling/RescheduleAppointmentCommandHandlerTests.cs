using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.RescheduleAppointment;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class RescheduleAppointmentCommandHandlerTests
{
    private const int UserId = 42;
    private const int AppointmentId = 99;
    private const int OtherAppointmentId = 100;
    private const int OtherPatientId = 500;
    private const int MinimumLeadDays = 4;
    private const int HorizonDays = 90;
    private const int MinimumBlockHours = 1;
    private const int BlockMarginHours = 1;
    private const int HoursBeforeStartPastDeadline = 47;

    // Hoy es lunes 4 de enero de 2027 a las 12:00 en Santo Domingo; la ventana del paciente empieza el 8 de enero.
    // La cita es el martes 12 a las 13:00 UTC y se reprograma al miércoles 20 a las 13:00 UTC.
    private static readonly DateTimeOffset Now = new(2027, 1, 4, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NewStartsAt = new(2027, 1, 20, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NewEndsAt = NewStartsAt.AddHours(AppointmentMother.DurationHours);
    private static readonly DateTimeOffset BeforeWindow = new(2027, 1, 5, 13, 0, 0, TimeSpan.Zero);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPatientRepository _patients = Substitute.For<IPatientRepository>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IActiveAppointmentReader _booked = Substitute.For<IActiveAppointmentReader>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = Now };
    private readonly Appointment _appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
    private readonly DateTimeOffset _previousStartsAt;
    private readonly RescheduleAppointmentCommandHandler _handler;

    public RescheduleAppointmentCommandHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Patient).Returns(true);
        _currentUser.UserId.Returns(UserId);
        var patient = PatientMother.WithRequiredData(UserId);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(patient, AppointmentMother.PatientId);
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(patient);

        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(_appointment, AppointmentId);
        _previousStartsAt = _appointment.StartsAt;
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(_appointment);

        _blocks.LockOpenBlockAsync(
                Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
        GivenBlocks(OpenBlock());
        GivenBooked();

        var options = Options.Create(new SchedulingOptions
        {
            TimeZone = DoctorAvailabilitySeriesMother.TimeZone,
            AppointmentDurationHours = AppointmentMother.DurationHours,
            MinimumBookingLeadDays = MinimumLeadDays,
            BookingHorizonDays = HorizonDays,
            PatientRescheduleDeadlineHours = AppointmentMother.PatientRescheduleDeadlineHours
        });
        _handler = new RescheduleAppointmentCommandHandler(
            _currentUser,
            _patients,
            _appointments,
            new AppointmentSlotChecker(_blocks, _booked),
            _clock,
            BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone),
            options);
    }

    [Fact]
    public async Task Reprograma_la_cita_propia_la_deja_pendiente_con_el_nuevo_horario_y_emite_el_evento()
    {
        var result = await Handle(NewStartsAt);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        _appointment.Status.ShouldBe(AppointmentStatus.Pending);
        _appointment.StartsAt.ShouldBe(NewStartsAt);
        _appointment.EndsAt.ShouldBe(NewEndsAt);
        var domainEvent = _appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRescheduledEvent>();
        domainEvent.Appointment.ShouldBeSameAs(_appointment);
        domainEvent.RescheduledBy.ShouldBe(AppointmentParticipant.Patient);
        domainEvent.PreviousStartsAt.ShouldBe(_previousStartsAt);
    }

    // Los casos del horario (bloqueo, lecturas, propia cita, sin bloque) los prueba AppointmentSlotCheckerTests; aquí
    // solo se comprueba que el handler propaga su resultado.
    [Fact]
    public async Task Si_el_horario_no_esta_libre_se_rechaza_como_horario_no_disponible()
    {
        GivenBooked(new BookedInterval(OtherAppointmentId, Range(NewStartsAt, NewEndsAt)));

        AssertRejected(await Handle(NewStartsAt), AppointmentErrors.SlotUnavailable);
    }

    [Fact]
    public async Task Un_error_del_dominio_se_informa_antes_que_el_horario_y_no_bloquea_nada()
    {
        _clock.UtcNow = _appointment.StartsAt.AddHours(-HoursBeforeStartPastDeadline);

        AssertRejected(await Handle(NewStartsAt), AppointmentErrors.RescheduleDeadlinePassed);
        _blocks.ReceivedCalls().ShouldBeEmpty();
        _booked.ReceivedCalls().ShouldBeEmpty();
        _appointment.StartsAt.ShouldBe(_previousStartsAt);
        _appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_nueva_hora_antes_de_la_ventana_de_reservas_se_rechaza_sin_bloquear_nada()
    {
        AssertRejected(await Handle(BeforeWindow), AppointmentErrors.TooSoon);
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Sin_autenticar_se_rechaza_sin_consultar_nada()
    {
        _currentUser.IsAuthenticated.Returns(false);

        AssertRejected(await Handle(NewStartsAt), AppointmentErrors.ReschedulePatientOnly);
        AssertNothingQueried();
    }

    [Fact]
    public async Task Sin_el_rol_de_paciente_se_rechaza_sin_consultar_nada()
    {
        _currentUser.IsInRole(UserRole.Patient).Returns(false);

        AssertRejected(await Handle(NewStartsAt), AppointmentErrors.ReschedulePatientOnly);
        AssertNothingQueried();
    }

    [Fact]
    public async Task Sin_perfil_de_paciente_se_rechaza()
    {
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Patient?)null);

        AssertRejected(await Handle(NewStartsAt), PatientErrors.ProfileNotFound);
    }

    [Fact]
    public async Task Una_cita_inexistente_se_rechaza_como_no_encontrada()
    {
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((Appointment?)null);

        AssertRejected(await Handle(NewStartsAt), AppointmentErrors.NotFound);
    }

    [Fact]
    public async Task Una_cita_de_otro_paciente_responde_igual_que_una_inexistente_y_no_cambia()
    {
        var foreign = AppointmentMother.InStatus(AppointmentStatus.Pending);
        typeof(Appointment).GetProperty(nameof(Appointment.PatientId))!.SetValue(foreign, OtherPatientId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(foreign);

        AssertRejected(await Handle(NewStartsAt), AppointmentErrors.NotFound);
        foreign.StartsAt.ShouldBe(_previousStartsAt);
        foreign.DomainEvents.ShouldBeEmpty();
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    private Task<Result<Unit>> Handle(DateTimeOffset startsAt) =>
        _handler.HandleAsync(new RescheduleAppointmentCommand(AppointmentId, startsAt), CancellationToken.None);

    // Bloque abierto que cubre el nuevo horario con un margen a cada lado.
    private static DoctorAvailability OpenBlock() => DoctorAvailability.Create(
        AppointmentMother.DoctorId,
        null,
        NewStartsAt.AddHours(-BlockMarginHours),
        NewEndsAt.AddHours(BlockMarginHours),
        MinimumBlockHours).Value;

    private void GivenBlocks(params DoctorAvailability[] blocks) =>
        _blocks.ListByDoctorAsync(
                Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DoctorAvailability>)blocks);

    private void GivenBooked(params BookedInterval[] intervals)
    {
        var byDoctor = new Dictionary<int, IReadOnlyList<BookedInterval>>();
        if (intervals.Length > 0) byDoctor[AppointmentMother.DoctorId] = intervals;

        _booked.ListActiveIntervalsAsync(
                Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<int, IReadOnlyList<BookedInterval>>)byDoctor);
    }

    private void AssertNothingQueried()
    {
        _patients.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
        _blocks.ReceivedCalls().ShouldBeEmpty();
        _booked.ReceivedCalls().ShouldBeEmpty();
    }

    private static TimeRange Range(DateTimeOffset start, DateTimeOffset end) => TimeRange.Create(start, end).Value;

    private static void AssertRejected(Result<Unit> result, Error error)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }
}
