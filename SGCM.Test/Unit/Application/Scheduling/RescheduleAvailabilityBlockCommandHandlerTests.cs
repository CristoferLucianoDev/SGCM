using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.RescheduleAvailabilityBlock;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Domain.Scheduling.Availability.Events;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class RescheduleAvailabilityBlockCommandHandlerTests
{
    private const int UserId = 42;
    private const int DoctorId = 7;
    private const int BlockId = 40;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);
    private static readonly BusinessCalendar Calendar = BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone);

    // Hoy es martes 5 de enero de 2027; el bloque es el miércoles 20, de 8:00 a 14:00 en Santo Domingo.
    private static readonly DateOnly Day = new(2027, 1, 20);
    private static readonly DateTimeOffset BlockStart = new(2027, 1, 20, 8, 0, 0, SantoDomingo);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDoctorRepository _doctors = Substitute.For<IDoctorRepository>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IAffectedAppointmentsPolicy _policy = Substitute.For<IAffectedAppointmentsPolicy>();
    private readonly IAffectedAppointmentsCanceler _canceler = Substitute.For<IAffectedAppointmentsCanceler>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2027, 1, 5, 10, 0, 0, SantoDomingo) };
    private readonly DoctorAvailability _block = Block(DoctorId, BlockId, BlockStart);
    private IReadOnlyCollection<TimeRange>? _evaluated;

    public RescheduleAvailabilityBlockCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        var doctor = DoctorMother.Create([1], userId: UserId).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(doctor, DoctorId);
        _doctors.GetByUserIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns(doctor);
        _blocks.GetByIdAsync(BlockId, Arg.Any<CancellationToken>()).Returns(_block);
        GivenOverlapping(_block);
        GivenAffected(0);
        GivenCancelled(0);
    }

    [Fact]
    public async Task Sin_citas_afectadas_cambia_el_horario_y_emite_el_evento_con_lo_que_deja_de_cubrir()
    {
        var result = await Handle(Command(10, 16));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new RescheduledAvailabilityBlockDto(0));
        _block.StartsAt.ShouldBe(BlockStart.AddHours(2));
        _block.EndsAt.ShouldBe(BlockStart.AddHours(8));
        _block.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DoctorAvailabilityRescheduledEvent>()
            .RemovedRanges.ShouldBe(new[] { Range(BlockStart, BlockStart.AddHours(2)) });
    }

    [Fact]
    public async Task La_politica_evalua_solo_lo_que_queda_fuera_del_horario_nuevo()
    {
        await Handle(Command(10, 16));

        _evaluated.ShouldBe(new[] { Range(BlockStart, BlockStart.AddHours(2)) });
    }

    [Fact]
    public async Task Con_citas_fuera_del_horario_nuevo_y_sin_motivo_se_rechaza_sin_cambiar_el_bloque()
    {
        GivenAffected(1);

        var result = await Handle(Command(10, 16));

        AssertRejected(result, AppointmentErrors.StatusReasonRequired);
    }

    [Fact]
    public async Task Con_citas_fuera_del_horario_nuevo_y_motivo_se_reprograma_con_ese_motivo()
    {
        GivenAffected(1);

        var result = await Handle(Command(10, 16) with { Reason = "Cambio de consultorio" });

        result.IsSuccess.ShouldBeTrue();
        _block.DomainEvents.OfType<DoctorAvailabilityRescheduledEvent>().ShouldHaveSingleItem()
            .Reason.ShouldBe("Cambio de consultorio");
    }

    [Fact]
    public async Task Cancela_las_citas_que_quedan_fuera_con_el_motivo_y_devuelve_cuantas()
    {
        GivenAffected(2);
        GivenCancelled(2);

        var result = await Handle(Command(10, 16) with { Reason = "Cambio de consultorio" });

        result.Value.ShouldBe(new RescheduledAvailabilityBlockDto(2));
        await _canceler.Received(1).CancelAsync(
            DoctorId,
            Arg.Is<IReadOnlyCollection<TimeRange>>(ranges => ranges.SequenceEqual(new[] { Range(BlockStart, BlockStart.AddHours(2)) })),
            "Cambio de consultorio",
            CancellationToken.None);
    }

    [Fact]
    public async Task Cancela_las_citas_antes_de_mover_el_bloque()
    {
        GivenAffected(1);
        var startWhenCancelling = default(DateTimeOffset);
        _canceler.CancelAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<TimeRange>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                startWhenCancelling = _block.StartsAt;
                return Result<int>.Success(1);
            });

        (await Handle(Command(10, 16) with { Reason = "Motivo" })).IsSuccess.ShouldBeTrue();

        startWhenCancelling.ShouldBe(BlockStart);
        _block.StartsAt.ShouldBe(BlockStart.AddHours(2));
    }

    [Fact]
    public async Task Si_una_cancelacion_falla_el_bloque_no_se_mueve()
    {
        GivenAffected(1);
        _canceler.CancelAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<TimeRange>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(AppointmentErrors.InvalidTransition));

        AssertRejected(await Handle(Command(10, 16) with { Reason = "Motivo" }), AppointmentErrors.InvalidTransition);
    }

    [Fact]
    public async Task Sin_motivo_obligatorio_no_cancela_nada()
    {
        GivenAffected(1);

        await Handle(Command(10, 16));

        _canceler.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task En_la_semana_actual_el_error_indica_que_el_bloque_solo_se_puede_cerrar()
    {
        var thisWeek = Block(DoctorId, BlockId, new DateTimeOffset(2027, 1, 8, 8, 0, 0, SantoDomingo));
        _blocks.GetByIdAsync(BlockId, Arg.Any<CancellationToken>()).Returns(thisWeek);

        var result = await Handle(new RescheduleAvailabilityBlockCommand(BlockId, new DateOnly(2027, 1, 8), new TimeOnly(10, 0), new TimeOnly(16, 0)));

        result.Error.ShouldBe(SchedulingErrors.CurrentWeekLocked);
        result.Error.Description.ShouldContain("solo se puede cerrar");
        _policy.ReceivedCalls().ShouldBeEmpty();
        _canceler.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_horario_nuevo_que_se_traslapa_con_otro_bloque_del_medico_se_rechaza()
    {
        GivenOverlapping(_block, Block(DoctorId, 41, BlockStart.AddHours(9)));

        var result = await Handle(Command(10, 16));

        AssertRejected(result, DoctorAvailabilityErrors.Overlap);
        _policy.ReceivedCalls().ShouldBeEmpty();
        _canceler.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task El_propio_bloque_no_cuenta_como_traslape()
    {
        (await Handle(Command(9, 15))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Bloquea_el_bloque_antes_de_buscar_traslapes_y_citas()
    {
        await Handle(Command(10, 16));

        Received.InOrder(() =>
        {
            _blocks.LockOpenBlockAsync(DoctorId, BlockStart, BlockStart.AddHours(6), CancellationToken.None);
            _blocks.ListByDoctorAsync(DoctorId, BlockStart.AddHours(2), BlockStart.AddHours(8), CancellationToken.None);
            _policy.EvaluateAsync(DoctorId, Arg.Any<IReadOnlyCollection<TimeRange>>(), CancellationToken.None);
            _canceler.CancelAsync(DoctorId, Arg.Any<IReadOnlyCollection<TimeRange>>(), null, CancellationToken.None);
        });
    }

    [Fact]
    public async Task Un_medico_solo_reprograma_sus_propios_bloques()
    {
        _blocks.GetByIdAsync(BlockId, Arg.Any<CancellationToken>()).Returns(Block(99, BlockId, BlockStart));

        AssertRejected(await Handle(Command(10, 16)), DoctorAvailabilityErrors.NotFound);
    }

    [Fact]
    public async Task Sin_perfil_de_medico_responde_medico_no_encontrado()
    {
        _doctors.GetByUserIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns((Doctor?)null);

        AssertRejected(await Handle(Command(10, 16)), DoctorErrors.NotFound);
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    private void AssertRejected(Result<RescheduledAvailabilityBlockDto> result, Error error)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        _block.StartsAt.ShouldBe(BlockStart);
        _block.EndsAt.ShouldBe(BlockStart.AddHours(6));
        _block.DomainEvents.ShouldBeEmpty();
    }

    private void GivenOverlapping(params DoctorAvailability[] blocks) =>
        _blocks.ListByDoctorAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DoctorAvailability>)blocks);

    private void GivenAffected(int count) =>
        _policy.EvaluateAsync(DoctorId, Arg.Do<IReadOnlyCollection<TimeRange>>(ranges => _evaluated = ranges), Arg.Any<CancellationToken>())
            .Returns(new AffectedAppointments(
                Enumerable.Range(1, count).Select(id => new BookedInterval(id, Range(BlockStart, BlockStart.AddHours(2)))).ToList(),
                IsShortNotice: false,
                new ShortNoticeCancellation(6, 30)));

    private void GivenCancelled(int count) =>
        _canceler.CancelAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<TimeRange>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(count));

    private static RescheduleAvailabilityBlockCommand Command(int startHour, int endHour) =>
        new(BlockId, Day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    private Task<Result<RescheduledAvailabilityBlockDto>> Handle(RescheduleAvailabilityBlockCommand command) =>
        new RescheduleAvailabilityBlockCommandHandler(
                _currentUser, _doctors, _blocks, _policy, _canceler, _clock, Calendar,
                Options.Create(new SchedulingOptions { MinimumBlockDurationHours = 6 }))
            .HandleAsync(command, CancellationToken.None);

    private static DoctorAvailability Block(int doctorId, int id, DateTimeOffset start)
    {
        var block = DoctorAvailability.Create(doctorId, null, start, start.AddHours(6), 6).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(block, id);
        return block;
    }

    private static TimeRange Range(DateTimeOffset start, DateTimeOffset end) => TimeRange.Create(start, end).Value;
}
