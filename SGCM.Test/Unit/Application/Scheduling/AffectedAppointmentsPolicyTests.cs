using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class AffectedAppointmentsPolicyTests
{
    private const int DoctorId = 7;
    private static readonly DateTimeOffset BlockStart = new(2027, 1, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly IActiveAppointmentReader _reader = Substitute.For<IActiveAppointmentReader>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = BlockStart.AddDays(-7) };

    [Fact]
    public async Task Lee_las_citas_del_medico_una_sola_vez_desde_el_primer_rango_hasta_el_ultimo()
    {
        GivenAppointments();
        using var cancellation = new CancellationTokenSource();

        await CreatePolicy().EvaluateAsync(DoctorId, [Range(0, 2), Range(4, 6)], cancellation.Token);

        await _reader.Received(1).ListActiveIntervalsAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { DoctorId })),
            BlockStart, BlockStart.AddHours(6), cancellation.Token);
    }

    [Fact]
    public async Task Aplica_el_umbral_y_las_palabras_minimas_de_la_configuracion()
    {
        GivenAppointments(new BookedInterval(1, Range(0, 2)));
        _clock.UtcNow = BlockStart.AddHours(-5);

        var affected = await CreatePolicy().EvaluateAsync(DoctorId, [Range(0, 6)], CancellationToken.None);

        affected.Appointments.ShouldHaveSingleItem().AppointmentId.ShouldBe(1);
        affected.IsShortNotice.ShouldBeTrue();
        affected.ShortNotice.ShouldBe(new ShortNoticeCancellation(6, 30));
    }

    [Fact]
    public async Task Descarta_las_citas_leidas_que_quedan_entre_dos_rangos_quitados()
    {
        GivenAppointments(new BookedInterval(1, Range(2, 4)), new BookedInterval(2, Range(4, 6)));

        var affected = await CreatePolicy().EvaluateAsync(DoctorId, [Range(0, 2), Range(4, 6)], CancellationToken.None);

        affected.Appointments.ShouldHaveSingleItem().AppointmentId.ShouldBe(2);
    }

    [Fact]
    public async Task Sin_rangos_no_hay_citas_afectadas_y_no_consulta_la_base()
    {
        var affected = await CreatePolicy().EvaluateAsync(DoctorId, [], CancellationToken.None);

        affected.Appointments.ShouldBeEmpty();
        affected.IsReasonRequired.ShouldBeFalse();
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_medico_sin_citas_no_tiene_citas_afectadas()
    {
        _reader.ListActiveIntervalsAsync(
                Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, IReadOnlyList<BookedInterval>>());

        (await CreatePolicy().EvaluateAsync(DoctorId, [Range(0, 6)], CancellationToken.None)).Appointments.ShouldBeEmpty();
    }

    private void GivenAppointments(params BookedInterval[] appointments) =>
        _reader.ListActiveIntervalsAsync(
                Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, IReadOnlyList<BookedInterval>> { [DoctorId] = appointments });

    private AffectedAppointmentsPolicy CreatePolicy() => new(
        _reader,
        _clock,
        Options.Create(new SchedulingOptions { DoctorLateCancellationThresholdHours = 6, DoctorLateCancellationMinimumWords = 30 }));

    private static TimeRange Range(int fromHour, int toHour) =>
        TimeRange.Create(BlockStart.AddHours(fromHour), BlockStart.AddHours(toHour)).Value;
}
