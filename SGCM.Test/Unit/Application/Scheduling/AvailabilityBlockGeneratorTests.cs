using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class AvailabilityBlockGeneratorTests
{
    private const int SeriesId = 15;
    private const int DoctorId = DoctorAvailabilitySeriesMother.DoctorId;

    // La serie del Mother: lunes, miércoles y viernes de 8:00 a 14:00 (12:00 a 18:00 UTC) desde el lunes 4 de enero de 2027.
    private static readonly DateOnly Monday = DoctorAvailabilitySeriesMother.StartDate;
    private static readonly DateTimeOffset MondayStartUtc = new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    // Lunes 4 de enero de 2027 a las 6:00 en Santo Domingo.
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2027, 1, 4, 10, 0, 0, TimeSpan.Zero) };
    private readonly SchedulingOptions _options = CreateAvailabilitySeriesCommandMother.Options();
    private List<DoctorAvailability>? _added;

    public AvailabilityBlockGeneratorTests()
    {
        _blocks.ListByDoctorAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailability>());
        _blocks.When(repository => repository.AddRange(Arg.Any<IEnumerable<DoctorAvailability>>()))
            .Do(call => _added = call.Arg<IEnumerable<DoctorAvailability>>().ToList());
    }

    [Fact]
    public async Task Genera_un_bloque_por_cada_dia_de_la_serie_desde_hoy_hasta_el_horizonte()
    {
        var generated = await Generate(Series());

        // Del lunes 4 de enero al domingo 4 de abril son 13 semanas: 39 lunes, miércoles y viernes.
        generated.ShouldBe(39);
        _added.ShouldNotBeNull();
        _added.Count.ShouldBe(39);
        _added.ShouldAllBe(block => block.SeriesId == SeriesId && block.DoctorId == DoctorId
            && block.IsAvailable && !block.IsModified);
        _added[0].StartsAt.ShouldBe(MondayStartUtc);
        _added[^1].StartsAt.ShouldBe(new DateTimeOffset(2027, 4, 2, 12, 0, 0, TimeSpan.Zero));
        _added.Select(block => block.StartsAt).ShouldBeInOrder();
    }

    [Fact]
    public async Task Consulta_los_bloques_del_medico_entre_la_primera_y_la_ultima_ocurrencia()
    {
        await Generate(Series());

        await _blocks.Received(1).ListByDoctorAsync(
            DoctorId, MondayStartUtc, new DateTimeOffset(2027, 4, 2, 18, 0, 0, TimeSpan.Zero), CancellationToken.None);
    }

    [Fact]
    public async Task Omite_los_dias_que_ya_tienen_un_bloque_de_la_serie_aunque_se_haya_editado()
    {
        ExistingBlocks(
            Block(SeriesId, MondayStartUtc),
            // El miércoles se editó a mano a otro horario: sigue contando como generado.
            Block(SeriesId, MondayStartUtc.AddDays(2).AddHours(2)));

        var generated = await Generate(Series());

        generated.ShouldBe(37);
        _added!.ShouldNotContain(block => block.StartsAt == MondayStartUtc);
        _added!.ShouldNotContain(block => block.StartsAt == MondayStartUtc.AddDays(2));
    }

    [Fact]
    public async Task Es_idempotente_si_todos_los_dias_ya_estan_generados_no_agrega_nada()
    {
        var series = Series();
        ExistingBlocks(series.GetOccurrences(Monday, Monday.AddDays(90))
            .Select(occurrence => Block(SeriesId, occurrence.StartsAt)).ToArray());

        var generated = await Generate(series);

        generated.ShouldBe(0);
        _blocks.DidNotReceive().AddRange(Arg.Any<IEnumerable<DoctorAvailability>>());
    }

    [Fact]
    public async Task Omite_los_dias_que_chocan_con_otro_bloque_del_medico_pero_no_los_contiguos()
    {
        ExistingBlocks(
            Block(null, MondayStartUtc.AddDays(4).AddHours(1)),
            Block(null, MondayStartUtc.AddDays(2).AddHours(6)));

        var generated = await Generate(Series());

        generated.ShouldBe(38);
        _added!.ShouldNotContain(block => block.StartsAt == MondayStartUtc.AddDays(4));
        _added!.ShouldContain(block => block.StartsAt == MondayStartUtc.AddDays(2));
    }

    [Fact]
    public async Task Una_serie_inactiva_no_genera_nada_ni_consulta_la_base()
    {
        var series = Series();
        series.Deactivate().IsSuccess.ShouldBeTrue();

        (await Generate(series)).ShouldBe(0);
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_serie_que_empieza_despues_de_hoy_genera_desde_su_fecha_desde()
    {
        // 1 de diciembre de 2026: el horizonte llega al lunes 1 de marzo de 2027.
        _clock.UtcNow = new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);

        var generated = await Generate(Series());

        // 8 semanas completas desde el 4 de enero (24 días) más el lunes 1 de marzo.
        generated.ShouldBe(25);
        _added![0].StartsAt.ShouldBe(MondayStartUtc);
    }

    [Fact]
    public async Task Una_serie_que_ya_termino_no_genera_nada_ni_consulta_la_base()
    {
        _clock.UtcNow = new DateTimeOffset(2027, 2, 1, 12, 0, 0, TimeSpan.Zero);

        (await Generate(Series(endDate: Monday.AddDays(13)))).ShouldBe(0);
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task El_horizonte_sale_de_la_configuracion()
    {
        _options.BookingHorizonDays = 6;

        (await Generate(Series())).ShouldBe(3);
    }

    [Fact]
    public async Task Hoy_se_cuenta_en_la_zona_de_la_serie()
    {
        // 5 de enero a las 03:00 UTC todavía es lunes 4 en Santo Domingo: el lunes también se genera.
        _clock.UtcNow = new DateTimeOffset(2027, 1, 5, 3, 0, 0, TimeSpan.Zero);

        await Generate(Series());

        _added![0].StartsAt.ShouldBe(MondayStartUtc);
    }

    private Task<int> Generate(DoctorAvailabilitySeries series) =>
        new AvailabilityBlockGenerator(_blocks, _clock, Options.Create(_options)).GenerateAsync(series, CancellationToken.None);

    private void ExistingBlocks(params DoctorAvailability[] blocks) =>
        _blocks.ListByDoctorAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(blocks.ToList());

    private static DoctorAvailabilitySeries Series(DateOnly? endDate = null)
    {
        var series = DoctorAvailabilitySeriesMother.Valid(endDate: endDate);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(series, SeriesId);
        return series;
    }

    private static DoctorAvailability Block(int? seriesId, DateTimeOffset startsAt) =>
        DoctorAvailability.Create(DoctorId, seriesId, startsAt, startsAt.AddHours(6), 6).Value;
}
