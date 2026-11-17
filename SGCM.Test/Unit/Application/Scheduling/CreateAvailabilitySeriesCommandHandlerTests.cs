using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class CreateAvailabilitySeriesCommandHandlerTests
{
    private const int UserId = 42;
    private const int DoctorId = 7;
    private const int SeriesId = 15;

    // Lunes 4 de enero de 2027 de 8:00 a 14:00 en Santo Domingo son las 12:00 a 18:00 UTC.
    private static readonly DateTimeOffset MondayStartUtc = new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDoctorRepository _doctors = Substitute.For<IDoctorRepository>();
    private readonly IDoctorAvailabilitySeriesRepository _series = Substitute.For<IDoctorAvailabilitySeriesRepository>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero) };
    private readonly SchedulingOptions _options = CreateAvailabilitySeriesCommandMother.Options();
    private DoctorAvailabilitySeries? _added;

    public CreateAvailabilitySeriesCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        var doctor = DoctorMother.Create([1]).Value;
        SetId(doctor, DoctorId);
        _doctors.GetByUserIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns(doctor);
        _series.ListActiveByDoctorAsync(DoctorId, Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailabilitySeries>());
        _blocks.ListByDoctorAsync(DoctorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailability>());
        _series.When(repository => repository.Add(Arg.Any<DoctorAvailabilitySeries>()))
            .Do(call => _added = call.Arg<DoctorAvailabilitySeries>());
        // Simula la base: el ID se genera al guardar.
        _unitOfWork.When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => SetId(_added!, SeriesId));
    }

    [Fact]
    public async Task Crea_la_serie_del_medico_de_la_sesion_con_la_configuracion_y_devuelve_su_id()
    {
        var result = await Handle(CreateAvailabilitySeriesCommandMother.Valid());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(SeriesId);
        _added.ShouldNotBeNull();
        _added.DoctorId.ShouldBe(DoctorId);
        _added.DaysOfWeek.ShouldBe(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday });
        _added.StartTime.ShouldBe(DoctorAvailabilitySeriesMother.StartTime);
        _added.EndTime.ShouldBe(DoctorAvailabilitySeriesMother.EndTime);
        _added.StartDate.ShouldBe(DoctorAvailabilitySeriesMother.StartDate);
        _added.EndDate.ShouldBeNull();
        _added.TimeZone.ShouldBe(DoctorAvailabilitySeriesMother.TimeZone);
        Received.InOrder(() =>
        {
            _doctors.GetByUserIdForUpdateAsync(UserId, CancellationToken.None);
            _series.Add(Arg.Any<DoctorAvailabilitySeries>());
            _unitOfWork.SaveChangesAsync(CancellationToken.None);
        });
    }

    [Fact]
    public async Task Busca_los_bloques_desde_el_dia_anterior_a_la_fecha_desde_y_sin_limite_si_no_hay_fecha_hasta()
    {
        await Handle(CreateAvailabilitySeriesCommandMother.Valid());

        await _blocks.Received(1).ListByDoctorAsync(
            DoctorId, new DateTimeOffset(2027, 1, 3, 0, 0, 0, TimeSpan.Zero), null, CancellationToken.None);
    }

    [Fact]
    public async Task Con_fecha_hasta_busca_los_bloques_hasta_dos_dias_despues()
    {
        await Handle(CreateAvailabilitySeriesCommandMother.Valid() with { EndDate = new DateOnly(2027, 1, 31) });

        await _blocks.Received(1).ListByDoctorAsync(
            DoctorId, new DateTimeOffset(2027, 1, 3, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 2, 2, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
    }

    [Fact]
    public async Task Una_fecha_desde_pasada_se_rechaza_sin_consultar_la_base()
    {
        var result = await Handle(CreateAvailabilitySeriesCommandMother.Valid() with { StartDate = new DateOnly(2026, 12, 31) });

        result.Error.ShouldBe(DoctorAvailabilitySeriesErrors.StartDateInPast);
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _series.ReceivedCalls().ShouldBeEmpty();
        _unitOfWork.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task La_fecha_de_hoy_se_acepta()
    {
        var result = await Handle(CreateAvailabilitySeriesCommandMother.Valid() with { StartDate = new DateOnly(2027, 1, 1) });

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Hoy_se_cuenta_en_la_zona_horaria_de_la_agenda_y_no_en_UTC()
    {
        // 2 de enero a las 02:00 UTC todavía es 1 de enero a las 22:00 en Santo Domingo.
        _clock.UtcNow = new DateTimeOffset(2027, 1, 2, 2, 0, 0, TimeSpan.Zero);

        var result = await Handle(CreateAvailabilitySeriesCommandMother.Valid() with { StartDate = new DateOnly(2027, 1, 1) });

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Sin_perfil_de_medico_responde_NotFound_sin_guardar()
    {
        _doctors.GetByUserIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns((Doctor?)null);

        var result = await Handle(CreateAvailabilitySeriesCommandMother.Valid());

        result.Error.ShouldBe(DoctorErrors.NotFound);
        _series.DidNotReceive().Add(Arg.Any<DoctorAvailabilitySeries>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_dia_invalido_se_rechaza_sin_consultar_la_base()
    {
        var result = await Handle(CreateAvailabilitySeriesCommandMother.Valid() with { DaysOfWeek = [1, 8] });

        result.Error.ShouldBe(DoctorAvailabilitySeriesErrors.InvalidDayOfWeek);
        _doctors.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_error_del_dominio_se_devuelve_sin_guardar()
    {
        var result = await Handle(CreateAvailabilitySeriesCommandMother.Valid() with { EndTime = new TimeOnly(10, 0) });

        result.Error.ShouldBe(DoctorAvailabilitySeriesErrors.DurationTooShort);
        _series.DidNotReceive().Add(Arg.Any<DoctorAvailabilitySeries>());
    }

    [Fact]
    public async Task La_duracion_minima_sale_de_la_configuracion()
    {
        _options.MinimumBlockDurationHours = 8;

        var result = await Handle(CreateAvailabilitySeriesCommandMother.Valid());

        result.Error.ShouldBe(DoctorAvailabilitySeriesErrors.DurationTooShort);
    }

    [Fact]
    public async Task Se_rechaza_si_se_traslapa_con_otra_serie_activa_del_medico()
    {
        _series.ListActiveByDoctorAsync(DoctorId, Arg.Any<CancellationToken>()).Returns(new List<DoctorAvailabilitySeries>
        {
            OtherSeries([DayOfWeek.Monday], new TimeOnly(10, 0), new TimeOnly(16, 0))
        });

        var result = await Handle(CreateAvailabilitySeriesCommandMother.Valid());

        result.Error.ShouldBe(DoctorAvailabilitySeriesErrors.Overlap);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        _series.DidNotReceive().Add(Arg.Any<DoctorAvailabilitySeries>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Otra_serie_activa_en_otros_dias_no_impide_crearla()
    {
        _series.ListActiveByDoctorAsync(DoctorId, Arg.Any<CancellationToken>()).Returns(new List<DoctorAvailabilitySeries>
        {
            OtherSeries([DayOfWeek.Tuesday, DayOfWeek.Thursday], new TimeOnly(8, 0), new TimeOnly(14, 0))
        });

        (await Handle(CreateAvailabilitySeriesCommandMother.Valid())).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Se_rechaza_si_se_traslapa_con_un_bloque_existente()
    {
        _blocks.ListByDoctorAsync(DoctorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailability> { Block(MondayStartUtc.AddHours(1)) });

        var result = await Handle(CreateAvailabilitySeriesCommandMother.Valid());

        result.Error.ShouldBe(DoctorAvailabilitySeriesErrors.Overlap);
        _series.DidNotReceive().Add(Arg.Any<DoctorAvailabilitySeries>());
    }

    [Fact]
    public async Task Bloques_contiguos_o_en_otros_dias_no_impiden_crearla()
    {
        _blocks.ListByDoctorAsync(DoctorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailability> { Block(MondayStartUtc.AddHours(6)), Block(MondayStartUtc.AddDays(1)) });

        (await Handle(CreateAvailabilitySeriesCommandMother.Valid())).IsSuccess.ShouldBeTrue();
    }

    private Task<Result<int>> Handle(CreateAvailabilitySeriesCommand command) =>
        new CreateAvailabilitySeriesCommandHandler(
                _currentUser, _doctors, _series, _blocks, _unitOfWork, _clock, Options.Create(_options))
            .HandleAsync(command, CancellationToken.None);

    private static DoctorAvailabilitySeries OtherSeries(DayOfWeek[] days, TimeOnly start, TimeOnly end) =>
        DoctorAvailabilitySeriesMother.Create(days, start, end, doctorId: DoctorId).Value;

    private static DoctorAvailability Block(DateTimeOffset startsAt) =>
        DoctorAvailability.Create(DoctorId, null, startsAt, startsAt.AddHours(6), 6).Value;

    private static void SetId(Entity entity, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
}
