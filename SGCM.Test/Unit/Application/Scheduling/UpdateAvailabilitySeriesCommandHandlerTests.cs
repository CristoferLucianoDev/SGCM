using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.UpdateAvailabilitySeries;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class UpdateAvailabilitySeriesCommandHandlerTests
{
    private const int UserId = 42;
    private const int DoctorId = DoctorAvailabilitySeriesMother.DoctorId;
    private const int SeriesId = 15;
    private const string Reason = "Cambio de consultorio";
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);
    private static readonly BusinessCalendar Calendar = BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone);

    // La serie del Mother es lunes, miércoles y viernes de 8:00 a 14:00 desde el lunes 4 de enero de 2027. Hoy es martes
    // 5, así que el cambio aplica desde el lunes 11; el miércoles 6 es de la semana actual.
    private static readonly DateTimeOffset Tuesday = Local(5, 10);
    private static readonly DateOnly NextMonday = new(2027, 1, 11);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDoctorRepository _doctors = Substitute.For<IDoctorRepository>();
    private readonly IDoctorAvailabilitySeriesRepository _seriesRepository = Substitute.For<IDoctorAvailabilitySeriesRepository>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IAffectedAppointmentsPolicy _policy = Substitute.For<IAffectedAppointmentsPolicy>();
    private readonly IAffectedAppointmentsCanceler _canceler = Substitute.For<IAffectedAppointmentsCanceler>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = Tuesday };
    private readonly SchedulingOptions _options = new()
    {
        TimeZone = DoctorAvailabilitySeriesMother.TimeZone,
        MinimumBlockDurationHours = DoctorAvailabilitySeriesMother.MinimumDurationHours,
        BookingHorizonDays = 14
    };

    private readonly DoctorAvailabilitySeries _series = WithId(DoctorAvailabilitySeriesMother.Valid(), SeriesId);
    private readonly DoctorAvailability _currentWednesday = Block(1, SeriesId, Local(6, 8));
    private readonly DoctorAvailability _monday = Block(2, SeriesId, Local(11, 8));
    private readonly DoctorAvailability _wednesday = Block(3, SeriesId, Local(13, 8));
    private readonly DoctorAvailability _friday = Block(4, SeriesId, Local(15, 8));
    private readonly List<DoctorAvailability> _existing;
    private List<DoctorAvailability>? _added;
    private IReadOnlyCollection<TimeRange>? _evaluated;

    public UpdateAvailabilitySeriesCommandHandlerTests()
    {
        _existing = [_currentWednesday, _monday, _wednesday, _friday];
        _currentUser.UserId.Returns(UserId);
        var doctor = WithId(DoctorMother.Create([1], userId: UserId).Value, DoctorId);
        _doctors.GetByUserIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns(doctor);
        _seriesRepository.GetByIdAsync(SeriesId, Arg.Any<CancellationToken>()).Returns(_series);
        _seriesRepository.ListActiveByDoctorAsync(DoctorId, Arg.Any<CancellationToken>()).Returns(new List<DoctorAvailabilitySeries> { _series });
        _blocks.ListByDoctorAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(_ => _existing.ToList());
        _blocks.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => _existing.SingleOrDefault(block => block.Id == call.Arg<int>()));
        _blocks.When(repository => repository.AddRange(Arg.Any<IEnumerable<DoctorAvailability>>()))
            .Do(call => _added = call.Arg<IEnumerable<DoctorAvailability>>().ToList());
        GivenAffected(0);
        GivenCancelled(0);
    }

    [Fact]
    public async Task Cambia_la_recurrencia_desde_el_lunes_siguiente_y_devuelve_esa_fecha()
    {
        var result = await Handle(Command());

        result.Value.ShouldBe(new UpdatedAvailabilitySeriesDto(NextMonday, 0));
        _series.DaysOfWeek.ShouldBe(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday });
        _series.StartTime.ShouldBe(new TimeOnly(10, 0));
        _series.EndTime.ShouldBe(new TimeOnly(16, 0));
        _series.StartDate.ShouldBe(NextMonday);
    }

    [Fact]
    public async Task La_semana_actual_no_cambia()
    {
        await Handle(Command());

        AssertHours(_currentWednesday, Local(6, 8), Local(6, 14));
        _blocks.DidNotReceive().Remove(_currentWednesday);
    }

    [Fact]
    public async Task Los_dias_que_siguen_toman_el_horario_nuevo_y_el_dia_que_sale_del_patron_se_borra()
    {
        await Handle(Command());

        AssertHours(_monday, Local(11, 10), Local(11, 16));
        AssertHours(_wednesday, Local(13, 10), Local(13, 16));
        _blocks.Received(1).Remove(_friday);
        _blocks.Received(1).Remove(Arg.Any<DoctorAvailability>());
    }

    [Fact]
    public async Task Genera_con_el_generador_los_bloques_de_los_dias_nuevos()
    {
        await Handle(Command());

        _added.ShouldNotBeNull();
        _added.Select(block => block.StartsAt).ShouldBe(new[] { Local(12, 10), Local(18, 10), Local(19, 10) });
        _added.ShouldAllBe(block => block.SeriesId == SeriesId && block.EndsAt - block.StartsAt == TimeSpan.FromHours(6));
    }

    [Fact]
    public async Task Guarda_los_cambios_antes_de_generar_porque_el_generador_lee_la_base()
    {
        await Handle(Command());

        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(CancellationToken.None);
            _blocks.AddRange(Arg.Any<IEnumerable<DoctorAvailability>>());
        });
    }

    [Fact]
    public async Task Un_bloque_editado_a_mano_conserva_su_horario()
    {
        _wednesday.RescheduleOccurrence(Local(13, 9), Local(13, 15), 6, Tuesday, Calendar).IsSuccess.ShouldBeTrue();
        _wednesday.ClearDomainEvents();

        await Handle(Command());

        AssertHours(_wednesday, Local(13, 9), Local(13, 15));
        _blocks.DidNotReceive().Remove(_wednesday);
        _evaluated!.ShouldNotContain(range => range.Start == Local(13, 8));
        _added!.ShouldNotContain(block => block.StartsAt == Local(13, 10));
    }

    [Fact]
    public async Task Un_bloque_cerrado_no_se_toca()
    {
        _friday.Close(null, Tuesday).IsSuccess.ShouldBeTrue();

        await Handle(Command());

        _blocks.DidNotReceive().Remove(_friday);
        AssertHours(_friday, Local(15, 8), Local(15, 14));
    }

    [Fact]
    public async Task La_politica_evalua_lo_que_queda_fuera_del_horario_nuevo()
    {
        await Handle(Command());

        _evaluated.ShouldNotBeNull();
        _evaluated.ShouldBe(new[]
        {
            Range(Local(11, 8), Local(11, 10)),
            Range(Local(13, 8), Local(13, 10)),
            Range(Local(15, 8), Local(15, 14))
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task Con_citas_fuera_y_sin_motivo_se_rechaza_sin_tocar_los_bloques()
    {
        GivenAffected(1);

        var result = await Handle(Command());

        result.Error.ShouldBe(AppointmentErrors.StatusReasonRequired);
        _canceler.ReceivedCalls().ShouldBeEmpty();
        _blocks.DidNotReceive().Remove(Arg.Any<DoctorAvailability>());
        _blocks.DidNotReceive().AddRange(Arg.Any<IEnumerable<DoctorAvailability>>());
        AssertHours(_monday, Local(11, 8), Local(11, 14));
    }

    [Fact]
    public async Task Cancela_las_citas_con_el_motivo_antes_de_mover_o_borrar_los_bloques()
    {
        GivenAffected(2);
        var mondayStartWhenCancelling = default(DateTimeOffset);
        _canceler.CancelAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<TimeRange>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                mondayStartWhenCancelling = _monday.StartsAt;
                _blocks.DidNotReceive().Remove(Arg.Any<DoctorAvailability>());
                return Result<int>.Success(2);
            });

        var result = await Handle(Command() with { Reason = Reason });

        result.Value.CancelledAppointments.ShouldBe(2);
        mondayStartWhenCancelling.ShouldBe(Local(11, 8));
        await _canceler.Received(1).CancelAsync(DoctorId, Arg.Any<IReadOnlyCollection<TimeRange>>(), Reason, CancellationToken.None);
    }

    [Fact]
    public async Task Si_una_cancelacion_falla_no_se_tocan_los_bloques()
    {
        GivenAffected(1);
        _canceler.CancelAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<TimeRange>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(AppointmentErrors.InvalidTransition));

        var result = await Handle(Command() with { Reason = Reason });

        result.Error.ShouldBe(AppointmentErrors.InvalidTransition);
        _blocks.DidNotReceive().Remove(Arg.Any<DoctorAvailability>());
        AssertHours(_monday, Local(11, 8), Local(11, 14));
    }

    [Fact]
    public async Task Un_jueves_el_cambio_aplica_desde_el_lunes_de_la_semana_despues()
    {
        _clock.UtcNow = Local(7, 10);

        var result = await Handle(Command());

        result.Value.EffectiveFrom.ShouldBe(new DateOnly(2027, 1, 18));
        AssertHours(_monday, Local(11, 8), Local(11, 14));
        AssertHours(_wednesday, Local(13, 8), Local(13, 14));
        _blocks.DidNotReceive().Remove(Arg.Any<DoctorAvailability>());
    }

    [Fact]
    public async Task Un_horario_que_se_traslapa_con_otra_serie_del_medico_se_rechaza()
    {
        var other = WithId(DoctorAvailabilitySeriesMother.Create([DayOfWeek.Tuesday], new TimeOnly(12, 0), new TimeOnly(18, 0)).Value, 16);
        _seriesRepository.ListActiveByDoctorAsync(DoctorId, Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailabilitySeries> { _series, other });

        (await Handle(Command())).Error.ShouldBe(DoctorAvailabilitySeriesErrors.Overlap);
        _policy.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_horario_que_se_traslapa_con_un_bloque_suelto_se_rechaza()
    {
        _existing.Add(Block(5, null, Local(12, 9)));

        (await Handle(Command())).Error.ShouldBe(DoctorAvailabilitySeriesErrors.Overlap);
        _blocks.DidNotReceive().Remove(Arg.Any<DoctorAvailability>());
    }

    [Fact]
    public async Task Una_fecha_hasta_anterior_al_lunes_siguiente_se_rechaza()
    {
        (await Handle(Command() with { EndDate = NextMonday.AddDays(-1) })).Error
            .ShouldBe(DoctorAvailabilitySeriesErrors.InvalidDateRange);
    }

    [Fact]
    public async Task Un_medico_solo_edita_sus_propias_series()
    {
        _seriesRepository.GetByIdAsync(SeriesId, Arg.Any<CancellationToken>())
            .Returns(WithId(DoctorAvailabilitySeriesMother.Create(doctorId: 99).Value, SeriesId));

        (await Handle(Command())).Error.ShouldBe(DoctorAvailabilitySeriesErrors.NotFound);
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_serie_inexistente_responde_serie_no_encontrada()
    {
        _seriesRepository.GetByIdAsync(SeriesId, Arg.Any<CancellationToken>()).Returns((DoctorAvailabilitySeries?)null);

        (await Handle(Command())).Error.ShouldBe(DoctorAvailabilitySeriesErrors.NotFound);
    }

    [Fact]
    public async Task Sin_perfil_de_medico_responde_medico_no_encontrado()
    {
        _doctors.GetByUserIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns((Doctor?)null);

        (await Handle(Command())).Error.ShouldBe(DoctorErrors.NotFound);
        _seriesRepository.ReceivedCalls().ShouldBeEmpty();
    }

    private static UpdateAvailabilitySeriesCommand Command() =>
        new(SeriesId, [1, 2, 3], new TimeOnly(10, 0), new TimeOnly(16, 0), EndDate: null);

    private Task<Result<UpdatedAvailabilitySeriesDto>> Handle(UpdateAvailabilitySeriesCommand command)
    {
        var options = Options.Create(_options);
        return new UpdateAvailabilitySeriesCommandHandler(
                _currentUser, _doctors, _seriesRepository, _blocks, _policy, _canceler,
                new AvailabilityBlockGenerator(_blocks, _clock, options), _unitOfWork, _clock, Calendar, options)
            .HandleAsync(command, CancellationToken.None);
    }

    private void GivenAffected(int count) =>
        _policy.EvaluateAsync(DoctorId, Arg.Do<IReadOnlyCollection<TimeRange>>(ranges => _evaluated = ranges.ToList()), Arg.Any<CancellationToken>())
            .Returns(new AffectedAppointments(
                Enumerable.Range(1, count).Select(id => new BookedInterval(id, Range(Local(11, 8), Local(11, 10)))).ToList(),
                IsShortNotice: false,
                new ShortNoticeCancellation(6, 30)));

    private void GivenCancelled(int count) =>
        _canceler.CancelAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<TimeRange>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(count));

    private static void AssertHours(DoctorAvailability block, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        block.StartsAt.ShouldBe(startsAt);
        block.EndsAt.ShouldBe(endsAt);
    }

    private static DoctorAvailability Block(int id, int? seriesId, DateTimeOffset startsAt) =>
        WithId(DoctorAvailability.Create(DoctorId, seriesId, startsAt, startsAt.AddHours(6), 6).Value, id);

    private static T WithId<T>(T entity, int id) where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
        return entity;
    }

    private static DateTimeOffset Local(int day, int hour) => new(2027, 1, day, hour, 0, 0, SantoDomingo);

    private static TimeRange Range(DateTimeOffset start, DateTimeOffset end) => TimeRange.Create(start, end).Value;
}
