using NSubstitute;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.CloseAvailabilityBlock;
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

public sealed class CloseAvailabilityBlockCommandHandlerTests
{
    private const int UserId = 42;
    private const int DoctorId = 7;
    private const int BlockId = 40;
    private const string Reason = "Congreso médico";
    private static readonly DateTimeOffset BlockStart = new(2027, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeRange BlockRange = TimeRange.Create(BlockStart, BlockStart.AddHours(6)).Value;
    private static readonly ShortNoticeCancellation ShortNotice = new(6, 30);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDoctorRepository _doctors = Substitute.For<IDoctorRepository>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IAffectedAppointmentsPolicy _policy = Substitute.For<IAffectedAppointmentsPolicy>();
    private readonly IAffectedAppointmentsCanceler _canceler = Substitute.For<IAffectedAppointmentsCanceler>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = BlockStart.AddDays(-3) };
    private readonly DoctorAvailability _block = Block(DoctorId);
    private bool _blockOpenWhenCancelling;

    public CloseAvailabilityBlockCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        var doctor = DoctorMother.Create([1], userId: UserId).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(doctor, DoctorId);
        _doctors.GetByUserIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns(doctor);
        _blocks.GetByIdAsync(BlockId, Arg.Any<CancellationToken>()).Returns(_block);
        GivenAffected(2);
        _canceler.CancelAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<TimeRange>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _blockOpenWhenCancelling = _block.IsAvailable;
                return Result<int>.Success(2);
            });
    }

    [Fact]
    public async Task Con_citas_activas_cancela_primero_las_citas_y_despues_cierra_el_bloque()
    {
        using var cancellation = new CancellationTokenSource();

        var result = await CreateHandler().HandleAsync(new CloseAvailabilityBlockCommand(BlockId, Reason), cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new ClosedAvailabilityBlockDto(2));
        _blockOpenWhenCancelling.ShouldBeTrue();
        _block.IsAvailable.ShouldBeFalse();
        _block.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DoctorAvailabilityClosedEvent>().Reason.ShouldBe(Reason);
        await _canceler.Received(1).CancelAsync(
            DoctorId, Arg.Is<IReadOnlyCollection<TimeRange>>(ranges => ranges.Single() == BlockRange), Reason, cancellation.Token);
    }

    [Fact]
    public async Task Bloquea_el_bloque_antes_de_evaluar_las_citas()
    {
        await CreateHandler().HandleAsync(new CloseAvailabilityBlockCommand(BlockId, Reason), CancellationToken.None);

        Received.InOrder(() =>
        {
            _blocks.LockOpenBlockAsync(DoctorId, BlockRange.Start, BlockRange.End, CancellationToken.None);
            _policy.EvaluateAsync(DoctorId, Arg.Any<IReadOnlyCollection<TimeRange>>(), CancellationToken.None);
            _canceler.CancelAsync(DoctorId, Arg.Any<IReadOnlyCollection<TimeRange>>(), Reason, CancellationToken.None);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Cerrar_un_bloque_con_citas_activas_sin_motivo_se_rechaza(string? reason)
    {
        var result = await CreateHandler().HandleAsync(new CloseAvailabilityBlockCommand(BlockId, reason), CancellationToken.None);

        AssertRejected(result, AppointmentErrors.StatusReasonRequired);
    }

    [Fact]
    public async Task Con_una_cita_a_menos_de_6_horas_exige_el_motivo_extendido()
    {
        GivenAffected(1, isShortNotice: true);

        var result = await CreateHandler().HandleAsync(new CloseAvailabilityBlockCommand(BlockId, Reason), CancellationToken.None);

        AssertRejected(result, AppointmentErrors.ShortNoticeReasonTooShort(30));
    }

    [Fact]
    public async Task Sin_citas_activas_cierra_sin_motivo()
    {
        GivenAffected(0);
        _canceler.CancelAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<TimeRange>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(0));

        var result = await CreateHandler().HandleAsync(new CloseAvailabilityBlockCommand(BlockId), CancellationToken.None);

        result.Value.ShouldBe(new ClosedAvailabilityBlockDto(0));
        _block.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Si_el_cancelador_falla_el_bloque_queda_abierto()
    {
        _canceler.CancelAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<TimeRange>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(AppointmentErrors.InvalidTransition));

        var result = await CreateHandler().HandleAsync(new CloseAvailabilityBlockCommand(BlockId, Reason), CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.InvalidTransition);
        _block.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_medico_solo_cierra_sus_propios_bloques()
    {
        _blocks.GetByIdAsync(BlockId, Arg.Any<CancellationToken>()).Returns(Block(99));

        var result = await CreateHandler().HandleAsync(new CloseAvailabilityBlockCommand(BlockId, Reason), CancellationToken.None);

        AssertRejected(result, DoctorAvailabilityErrors.NotFound);
        await _blocks.DidNotReceive().LockOpenBlockAsync(
            Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_bloque_que_no_existe_responde_no_encontrado()
    {
        AssertRejected(
            await CreateHandler().HandleAsync(new CloseAvailabilityBlockCommand(55, Reason), CancellationToken.None),
            DoctorAvailabilityErrors.NotFound);
    }

    [Fact]
    public async Task Un_bloque_ya_cerrado_se_rechaza_sin_cancelar_citas()
    {
        _block.Close(null, _clock.UtcNow);
        _block.ClearDomainEvents();

        var result = await CreateHandler().HandleAsync(new CloseAvailabilityBlockCommand(BlockId, Reason), CancellationToken.None);

        result.Error.ShouldBe(DoctorAvailabilityErrors.AlreadyClosed);
        _policy.ReceivedCalls().ShouldBeEmpty();
        _canceler.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_bloque_que_ya_termino_se_rechaza_sin_cancelar_citas()
    {
        _clock.UtcNow = BlockStart.AddHours(7);

        var result = await CreateHandler().HandleAsync(new CloseAvailabilityBlockCommand(BlockId, Reason), CancellationToken.None);

        AssertRejected(result, DoctorAvailabilityErrors.AlreadyEnded);
    }

    [Fact]
    public async Task Sin_perfil_de_medico_responde_medico_no_encontrado()
    {
        _doctors.GetByUserIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns((Doctor?)null);

        AssertRejected(
            await CreateHandler().HandleAsync(new CloseAvailabilityBlockCommand(BlockId, Reason), CancellationToken.None),
            DoctorErrors.NotFound);
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    private void AssertRejected(Result<ClosedAvailabilityBlockDto> result, Error error)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        _canceler.ReceivedCalls().ShouldBeEmpty();
        _block.IsAvailable.ShouldBeTrue();
        _block.DomainEvents.ShouldBeEmpty();
    }

    private void GivenAffected(int count, bool isShortNotice = false) =>
        _policy.EvaluateAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<TimeRange>>(), Arg.Any<CancellationToken>())
            .Returns(new AffectedAppointments(
                Enumerable.Range(1, count)
                    .Select(id => new BookedInterval(id, TimeRange.Create(BlockStart, BlockStart.AddHours(2)).Value)).ToList(),
                isShortNotice,
                ShortNotice));

    private CloseAvailabilityBlockCommandHandler CreateHandler() =>
        new(_currentUser, _doctors, _blocks, _policy, _canceler, _clock);

    private static DoctorAvailability Block(int doctorId)
    {
        var block = DoctorAvailability.Create(doctorId, null, BlockStart, BlockStart.AddHours(6), 6).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(block, BlockId);
        return block;
    }
}
