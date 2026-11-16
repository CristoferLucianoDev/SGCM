using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

public sealed class DoctorAvailabilityCreateTests
{
    private const int DoctorId = DoctorAvailabilitySeriesMother.DoctorId;
    private const int MinimumHours = DoctorAvailabilitySeriesMother.MinimumDurationHours;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);
    private static readonly DateTimeOffset Start = new(2027, 1, 4, 8, 0, 0, SantoDomingo);
    private static readonly DateTimeOffset End = new(2027, 1, 4, 14, 0, 0, SantoDomingo);

    [Fact]
    public void Un_bloque_suelto_queda_disponible_sin_serie_y_sin_marca_de_modificado()
    {
        var result = DoctorAvailability.Create(DoctorId, null, Start, End, MinimumHours);

        result.IsSuccess.ShouldBeTrue();
        var block = result.Value;
        block.Id.ShouldBe(0);
        block.DoctorId.ShouldBe(DoctorId);
        block.SeriesId.ShouldBeNull();
        block.IsAvailable.ShouldBeTrue();
        block.IsModified.ShouldBeFalse();
        block.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Las_fechas_del_bloque_se_guardan_en_UTC()
    {
        var block = DoctorAvailability.Create(DoctorId, null, Start, End, MinimumHours).Value;

        block.StartsAt.ShouldBe(new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.Zero));
        block.StartsAt.Offset.ShouldBe(TimeSpan.Zero);
        block.EndsAt.ShouldBe(new DateTimeOffset(2027, 1, 4, 18, 0, 0, TimeSpan.Zero));
        block.EndsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Un_bloque_de_serie_conserva_la_serie_y_queda_disponible_sin_marca_de_modificado()
    {
        var block = DoctorAvailability.Create(DoctorId, 5, Start, End, MinimumHours).Value;

        block.SeriesId.ShouldBe(5);
        block.IsAvailable.ShouldBeTrue();
        block.IsModified.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_medico_no_positivo_se_rechaza(int doctorId) =>
        AssertRejected(DoctorAvailability.Create(doctorId, null, Start, End, MinimumHours), DoctorAvailabilityErrors.InvalidDoctor);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_serie_no_positiva_se_rechaza(int seriesId) =>
        AssertRejected(DoctorAvailability.Create(DoctorId, seriesId, Start, End, MinimumHours), DoctorAvailabilityErrors.InvalidSeries);

    [Theory]
    [InlineData(30, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void Un_inicio_que_no_esta_en_punto_se_rechaza(int minutes, int seconds, int milliseconds) =>
        AssertRejected(
            DoctorAvailability.Create(DoctorId, null,
                Start.AddMinutes(minutes).AddSeconds(seconds).AddMilliseconds(milliseconds), End, MinimumHours),
            DoctorAvailabilityErrors.NotOnHour);

    [Fact]
    public void Un_fin_que_no_esta_en_punto_se_rechaza() =>
        AssertRejected(DoctorAvailability.Create(DoctorId, null, Start, End.AddMinutes(-15), MinimumHours),
            DoctorAvailabilityErrors.NotOnHour);

    [Fact]
    public void En_punto_se_mide_en_UTC_como_en_la_base()
    {
        // 8:00 en la India (UTC+5:30) son las 2:30 UTC: no está en punto.
        var india = TimeSpan.FromHours(5.5);

        AssertRejected(
            DoctorAvailability.Create(DoctorId, null,
                new DateTimeOffset(2027, 1, 4, 8, 0, 0, india), new DateTimeOffset(2027, 1, 4, 14, 0, 0, india), MinimumHours),
            DoctorAvailabilityErrors.NotOnHour);
    }

    [Fact]
    public void Un_fin_anterior_o_igual_al_inicio_se_rechaza()
    {
        AssertRejected(DoctorAvailability.Create(DoctorId, null, End, Start, MinimumHours), DoctorAvailabilityErrors.InvalidTimeRange);
        AssertRejected(DoctorAvailability.Create(DoctorId, null, Start, Start, MinimumHours), DoctorAvailabilityErrors.InvalidTimeRange);
    }

    [Fact]
    public void Un_bloque_de_menos_de_la_duracion_minima_se_rechaza() =>
        AssertRejected(DoctorAvailability.Create(DoctorId, null, Start, Start.AddHours(MinimumHours - 1), MinimumHours),
            DoctorAvailabilityErrors.DurationTooShort);

    [Fact]
    public void Un_bloque_de_exactamente_la_duracion_minima_se_acepta()
    {
        DoctorAvailability.Create(DoctorId, null, Start, Start.AddHours(MinimumHours), MinimumHours).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_duracion_minima_no_positiva_es_un_error_de_programacion(int hours)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DoctorAvailability.Create(DoctorId, null, Start, End, hours));
    }

    [Fact]
    public void El_bloque_de_una_ocurrencia_toma_el_medico_la_serie_y_las_horas()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(series, 15);
        var occurrence = series.GetOccurrences(series.StartDate, series.StartDate)[0];

        var block = DoctorAvailability.CreateFromSeries(series, occurrence, MinimumHours).Value;

        block.DoctorId.ShouldBe(series.DoctorId);
        block.SeriesId.ShouldBe(15);
        block.StartsAt.ShouldBe(occurrence.StartsAt);
        block.EndsAt.ShouldBe(occurrence.EndsAt);
        block.IsAvailable.ShouldBeTrue();
        block.IsModified.ShouldBeFalse();
    }

    [Fact]
    public void Una_serie_sin_guardar_no_puede_generar_bloques()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();
        var occurrence = series.GetOccurrences(series.StartDate, series.StartDate)[0];

        AssertRejected(DoctorAvailability.CreateFromSeries(series, occurrence, MinimumHours), DoctorAvailabilityErrors.InvalidSeries);
    }

    private static void AssertRejected(Result<DoctorAvailability> result, Error expected)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(expected);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }
}
