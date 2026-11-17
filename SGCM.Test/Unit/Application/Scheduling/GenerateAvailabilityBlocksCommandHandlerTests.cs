using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.GenerateAvailabilityBlocks;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GenerateAvailabilityBlocksCommandHandlerTests
{
    private readonly IDoctorAvailabilitySeriesRepository _series = Substitute.For<IDoctorAvailabilitySeriesRepository>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2027, 1, 4, 10, 0, 0, TimeSpan.Zero) };

    public GenerateAvailabilityBlocksCommandHandlerTests()
    {
        _blocks.ListByDoctorAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailability>());
    }

    [Fact]
    public async Task Genera_los_bloques_de_todas_las_series_activas_y_devuelve_cuantos_agrego()
    {
        _series.ListActiveFromAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<DoctorAvailabilitySeries>
        {
            Series(15, DoctorAvailabilitySeriesMother.DoctorId, [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]),
            Series(16, 8, [DayOfWeek.Tuesday])
        });

        var result = await Handle();

        // 39 bloques de la primera serie y 13 martes de la segunda hasta el domingo 4 de abril.
        result.Value.ShouldBe(52);
        _blocks.Received(2).AddRange(Arg.Any<IEnumerable<DoctorAvailability>>());
    }

    [Fact]
    public async Task Busca_las_series_vigentes_desde_el_dia_anterior_a_la_fecha_UTC()
    {
        _series.ListActiveFromAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<DoctorAvailabilitySeries>());

        var result = await Handle();

        result.Value.ShouldBe(0);
        await _series.Received(1).ListActiveFromAsync(new DateOnly(2027, 1, 3), CancellationToken.None);
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    private Task<Result<int>> Handle()
    {
        var generator = new AvailabilityBlockGenerator(
            _blocks, _clock, Options.Create(CreateAvailabilitySeriesCommandMother.Options()));

        return new GenerateAvailabilityBlocksCommandHandler(_series, generator, _clock)
            .HandleAsync(new GenerateAvailabilityBlocksCommand(), CancellationToken.None);
    }

    private static DoctorAvailabilitySeries Series(int id, int doctorId, DayOfWeek[] days)
    {
        var series = DoctorAvailabilitySeriesMother.Create(days, doctorId: doctorId).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(series, id);
        return series;
    }
}
