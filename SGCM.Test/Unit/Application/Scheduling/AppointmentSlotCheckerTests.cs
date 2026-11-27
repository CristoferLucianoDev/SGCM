using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class AppointmentSlotCheckerTests
{
    private const int AppointmentId = 99;
    private const int OtherAppointmentId = 100;
    private const int ThreeDaysHours = 72;
    private const int MinimumBlockHours = 1;
    private const int BlockMarginHours = 1;

    // La cita se movió al miércoles 20 de enero de 2027 a las 13:00 UTC.
    private static readonly DateTimeOffset NewStartsAt = new(2027, 1, 20, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NewEndsAt = NewStartsAt.AddHours(AppointmentMother.DurationHours);

    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IActiveAppointmentReader _booked = Substitute.For<IActiveAppointmentReader>();
    private readonly Appointment _appointment = MovedAppointment();
    private readonly AppointmentSlotChecker _checker;

    public AppointmentSlotCheckerTests()
    {
        _blocks.LockOpenBlockAsync(
                Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
        GivenBlocks(OpenBlock());
        GivenBooked();
        _checker = new AppointmentSlotChecker(_blocks, _booked);
    }

    [Fact]
    public async Task Un_horario_dentro_de_un_bloque_abierto_y_sin_otras_citas_esta_libre()
    {
        (await IsFree()).ShouldBeTrue();
    }

    [Fact]
    public async Task Pasa_al_bloqueo_y_a_las_lecturas_el_medico_el_inicio_el_fin_y_el_token()
    {
        using var cancellation = new CancellationTokenSource();

        await _checker.IsFreeForAsync(_appointment, AppointmentMother.DurationHours, cancellation.Token);

        await _blocks.Received(1).LockOpenBlockAsync(
            AppointmentMother.DoctorId, NewStartsAt, NewEndsAt, cancellation.Token);
        await _blocks.Received(1).ListByDoctorAsync(
            AppointmentMother.DoctorId, NewStartsAt, NewEndsAt, cancellation.Token);
        await _booked.Received(1).ListActiveIntervalsAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { AppointmentMother.DoctorId })),
            NewStartsAt,
            NewEndsAt,
            cancellation.Token);
    }

    [Fact]
    public async Task Otra_cita_activa_en_el_horario_lo_ocupa()
    {
        GivenBooked(new BookedInterval(OtherAppointmentId, Range(NewStartsAt, NewEndsAt)));

        (await IsFree()).ShouldBeFalse();
    }

    [Fact]
    public async Task La_propia_cita_leida_en_el_horario_no_lo_ocupa()
    {
        GivenBooked(new BookedInterval(AppointmentId, Range(NewStartsAt, NewEndsAt)));

        (await IsFree()).ShouldBeTrue();
    }

    [Fact]
    public async Task Sin_bloque_abierto_en_el_bloqueo_no_esta_libre_y_no_se_leen_citas()
    {
        _blocks.LockOpenBlockAsync(
                Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        (await IsFree()).ShouldBeFalse();
        _booked.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Si_las_lecturas_no_devuelven_un_bloque_abierto_que_cubra_el_horario_no_esta_libre()
    {
        GivenBlocks();

        (await IsFree()).ShouldBeFalse();
    }

    [Fact]
    public async Task Un_bloque_cerrado_que_cubre_el_horario_se_ignora()
    {
        var closed = OpenBlock();
        closed.Close(null, NewStartsAt.AddDays(-1)).IsSuccess.ShouldBeTrue();
        GivenBlocks(closed);

        (await IsFree()).ShouldBeFalse();
    }

    private Task<bool> IsFree() =>
        _checker.IsFreeForAsync(_appointment, AppointmentMother.DurationHours, CancellationToken.None);

    // Cita con Id, movida en memoria al nuevo horario como lo hace la reprogramación.
    private static Appointment MovedAppointment()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(appointment, AppointmentId);
        appointment.RescheduleByPatient(
                NewStartsAt,
                AppointmentMother.StartsAt.AddHours(-ThreeDaysHours),
                AppointmentMother.PatientRescheduleDeadlineHours,
                AppointmentMother.DurationHours,
                AppointmentMother.BookingWindow)
            .IsSuccess.ShouldBeTrue();
        return appointment;
    }

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

    private static TimeRange Range(DateTimeOffset start, DateTimeOffset end) => TimeRange.Create(start, end).Value;
}
