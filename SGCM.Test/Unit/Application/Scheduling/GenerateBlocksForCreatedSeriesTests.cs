using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.EventHandlers;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Domain.Scheduling.Availability.Events;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GenerateBlocksForCreatedSeriesTests
{
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2027, 1, 4, 10, 0, 0, TimeSpan.Zero) };

    public GenerateBlocksForCreatedSeriesTests()
    {
        _blocks.ListByDoctorAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailability>());
    }

    [Fact]
    public async Task La_serie_creada_genera_sus_bloques_y_los_guarda()
    {
        var series = SavedSeries();

        await Handle(series);

        _blocks.Received(1).AddRange(Arg.Is<IEnumerable<DoctorAvailability>>(blocks =>
            blocks.Count() == 39 && blocks.All(block => block.SeriesId == 15)));
        await _unitOfWork.Received(1).SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Si_no_hay_bloques_que_generar_no_guarda()
    {
        _clock.UtcNow = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        await Handle(SavedSeries(endDate: new DateOnly(2027, 2, 1)));

        _blocks.DidNotReceive().AddRange(Arg.Any<IEnumerable<DoctorAvailability>>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private Task Handle(DoctorAvailabilitySeries series)
    {
        var generator = new AvailabilityBlockGenerator(
            _blocks, _clock, Options.Create(CreateAvailabilitySeriesCommandMother.Options()));
        var created = series.DomainEvents.OfType<DoctorAvailabilitySeriesCreatedEvent>().Single();

        return new GenerateBlocksForCreatedSeries(generator, _unitOfWork).HandleAsync(created, CancellationToken.None);
    }

    private static DoctorAvailabilitySeries SavedSeries(DateOnly? endDate = null)
    {
        var series = DoctorAvailabilitySeriesMother.Valid(endDate: endDate);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(series, 15);
        return series;
    }
}
