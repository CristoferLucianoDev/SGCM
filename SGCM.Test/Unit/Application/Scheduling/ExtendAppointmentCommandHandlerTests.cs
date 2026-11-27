using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.ExtendAppointment;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class ExtendAppointmentCommandHandlerTests
{
    private const int UserId = 42;
    private const int AppointmentId = 99;
    private const int OtherAppointmentId = 100;
    private const int OtherDoctorId = 500;
    private const int HoursIntoTheAppointment = 1;
    private const int ExtraHours = 1;
    private const int MinimumBlockHours = 1;
    private const int BlockMarginHours = 2;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IActiveAppointmentReader _booked = Substitute.For<IActiveAppointmentReader>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly Appointment _appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
    private readonly DateTimeOffset _startsAt;
    private readonly DateTimeOffset _endsAt;
    private readonly ExtendAppointmentCommandHandler _handler;

    public ExtendAppointmentCommandHandlerTests()
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(_appointment, AppointmentId);
        _startsAt = _appointment.StartsAt;
        _endsAt = _appointment.EndsAt;
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((int?)AppointmentMother.DoctorId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(_appointment);
        _clock.UtcNow = _startsAt.AddHours(HoursIntoTheAppointment);
        GivenBlocks(OpenBlock());
        GivenBooked();

        var access = new DoctorAppointmentAccess(_currentUser, _doctors, _appointments);
        _handler = new ExtendAppointmentCommandHandler(access, _blocks, _booked, _clock);
    }

    [Fact]
    public async Task Extiende_la_cita_en_curso_cuando_el_bloque_cubre_el_tramo_y_no_hay_otras_citas()
    {
        var result = await Handle();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        _appointment.EndsAt.ShouldBe(_endsAt.AddHours(ExtraHours));
        _appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Pasa_a_las_lecturas_el_medico_el_rango_y_el_token()
    {
        using var cancellation = new CancellationTokenSource();
        var newEnd = _endsAt.AddHours(ExtraHours);

        await _handler.HandleAsync(new ExtendAppointmentCommand(AppointmentId, ExtraHours), cancellation.Token);

        // Los bloques se leen desde el inicio de la cita porque la base exige que la cita completa quepa en uno solo.
        await _blocks.Received(1).ListByDoctorAsync(AppointmentMother.DoctorId, _startsAt, newEnd, cancellation.Token);
        await _booked.Received(1).ListActiveIntervalsAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { AppointmentMother.DoctorId })),
            _endsAt,
            newEnd,
            cancellation.Token);
    }

    [Fact]
    public async Task Ignora_los_bloques_cerrados_aunque_cubran_el_tramo()
    {
        var closed = OpenBlock();
        closed.Close(null, _clock.UtcNow);
        GivenBlocks(closed);

        AssertRejected(await Handle(), AppointmentErrors.ExtensionUnavailable);
        _appointment.EndsAt.ShouldBe(_endsAt);
    }

    [Fact]
    public async Task Excluye_la_propia_cita_de_las_citas_leidas()
    {
        GivenBooked(new BookedInterval(AppointmentId, Range(_startsAt, _endsAt.AddHours(ExtraHours))));

        var result = await Handle();

        result.IsSuccess.ShouldBeTrue();
        _appointment.EndsAt.ShouldBe(_endsAt.AddHours(ExtraHours));
    }

    [Fact]
    public async Task Otra_cita_en_el_tramo_impide_extender()
    {
        GivenBooked(new BookedInterval(OtherAppointmentId, Range(_endsAt, _endsAt.AddHours(ExtraHours))));

        AssertRejected(await Handle(), AppointmentErrors.ExtensionUnavailable);
        _appointment.EndsAt.ShouldBe(_endsAt);
        _appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_cita_que_no_esta_en_curso_se_rechaza_y_no_cambia()
    {
        _clock.UtcNow = _startsAt.AddSeconds(-1);

        AssertRejected(await Handle(), AppointmentErrors.NotInProgress);
        _appointment.EndsAt.ShouldBe(_endsAt);
    }

    [Fact]
    public async Task Sin_autenticar_se_rechaza_con_el_error_de_progreso_sin_consultar_nada()
    {
        _currentUser.IsAuthenticated.Returns(false);

        AssertRejected(await Handle(), AppointmentErrors.ProgressDoctorOnly);
        AssertNothingRead();
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_se_rechaza_sin_leer_bloques_ni_citas()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        AssertRejected(await Handle(), DoctorErrors.NotFound);
        AssertNothingRead();
    }

    [Fact]
    public async Task Una_cita_inexistente_se_rechaza_como_no_encontrada_sin_leer_bloques_ni_citas()
    {
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((Appointment?)null);

        AssertRejected(await Handle(), AppointmentErrors.NotFound);
        AssertNothingRead();
    }

    [Fact]
    public async Task Una_cita_de_otro_medico_responde_igual_que_una_inexistente_y_no_cambia()
    {
        var foreign = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        typeof(Appointment).GetProperty(nameof(Appointment.DoctorId))!.SetValue(foreign, OtherDoctorId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(foreign);

        AssertRejected(await Handle(), AppointmentErrors.NotFound);
        foreign.EndsAt.ShouldBe(_endsAt);
        AssertNothingRead();
    }

    private Task<Result<Unit>> Handle() =>
        _handler.HandleAsync(new ExtendAppointmentCommand(AppointmentId, ExtraHours), CancellationToken.None);

    // Bloque abierto que cubre la cita completa y el tramo nuevo, con margen de sobra.
    private DoctorAvailability OpenBlock() => DoctorAvailability.Create(
        AppointmentMother.DoctorId, null, _startsAt, _endsAt.AddHours(BlockMarginHours), MinimumBlockHours).Value;

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

    private void AssertNothingRead()
    {
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
