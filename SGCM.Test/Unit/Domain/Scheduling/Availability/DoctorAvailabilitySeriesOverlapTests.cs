using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

public sealed class DoctorAvailabilitySeriesOverlapTests
{
    // La serie del Mother: lunes, miércoles y viernes de 8:00 a 14:00 (12:00 a 18:00 UTC) desde el lunes 4 de enero de 2027.
    private static readonly DateOnly Monday = DoctorAvailabilitySeriesMother.StartDate;
    private static readonly DateTimeOffset MondayStartUtc = new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Dos_series_con_un_dia_en_comun_y_horas_que_se_cruzan_se_traslapan()
    {
        var other = Series([DayOfWeek.Monday], new TimeOnly(10, 0), new TimeOnly(16, 0));

        DoctorAvailabilitySeriesMother.Valid().OverlapsWith(other).ShouldBeTrue();
        other.OverlapsWith(DoctorAvailabilitySeriesMother.Valid()).ShouldBeTrue();
    }

    [Fact]
    public void Dos_series_en_dias_distintos_no_se_traslapan()
    {
        var other = Series([DayOfWeek.Tuesday, DayOfWeek.Thursday], new TimeOnly(8, 0), new TimeOnly(14, 0));

        DoctorAvailabilitySeriesMother.Valid().OverlapsWith(other).ShouldBeFalse();
    }

    [Fact]
    public void Dos_series_contiguas_el_mismo_dia_no_se_traslapan()
    {
        var other = Series([DayOfWeek.Monday], new TimeOnly(14, 0), new TimeOnly(20, 0));

        DoctorAvailabilitySeriesMother.Valid().OverlapsWith(other).ShouldBeFalse();
    }

    [Fact]
    public void Una_serie_que_termina_antes_de_que_empiece_la_otra_no_se_traslapa()
    {
        var earlier = Series([DayOfWeek.Monday], new TimeOnly(8, 0), new TimeOnly(14, 0),
            Monday.AddDays(-30), Monday.AddDays(-1));

        DoctorAvailabilitySeriesMother.Valid().OverlapsWith(earlier).ShouldBeFalse();
    }

    [Fact]
    public void Un_tramo_comun_corto_sin_el_dia_compartido_no_se_traslapa()
    {
        // Ambas incluyen el viernes, pero solo comparten del lunes 4 al miércoles 6.
        var series = DoctorAvailabilitySeriesMother.Valid(endDate: Monday.AddDays(2));
        var other = Series([DayOfWeek.Friday], new TimeOnly(8, 0), new TimeOnly(14, 0), Monday.AddDays(-14), Monday.AddDays(2));

        series.OverlapsWith(other).ShouldBeFalse();
    }

    [Fact]
    public void Una_serie_que_empieza_meses_despues_y_comparte_dia_se_traslapa()
    {
        var later = Series([DayOfWeek.Friday], new TimeOnly(13, 0), new TimeOnly(19, 0), Monday.AddMonths(6), null);

        DoctorAvailabilitySeriesMother.Valid().OverlapsWith(later).ShouldBeTrue();
    }

    [Fact]
    public void Un_bloque_que_se_cruza_con_una_ocurrencia_se_traslapa()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();

        series.OverlapsWith(MondayStartUtc.AddHours(3), MondayStartUtc.AddHours(9)).ShouldBeTrue();
        series.OverlapsWith(MondayStartUtc.AddHours(-2), MondayStartUtc.AddHours(1)).ShouldBeTrue();
    }

    [Fact]
    public void Un_bloque_contiguo_o_en_otro_dia_no_se_traslapa()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();

        series.OverlapsWith(MondayStartUtc.AddHours(6), MondayStartUtc.AddHours(12)).ShouldBeFalse();
        series.OverlapsWith(MondayStartUtc.AddHours(-6), MondayStartUtc).ShouldBeFalse();
        series.OverlapsWith(MondayStartUtc.AddDays(1), MondayStartUtc.AddDays(1).AddHours(6)).ShouldBeFalse();
    }

    [Fact]
    public void Un_bloque_antes_de_la_fecha_desde_no_se_traslapa()
    {
        DoctorAvailabilitySeriesMother.Valid()
            .OverlapsWith(MondayStartUtc.AddDays(-7), MondayStartUtc.AddDays(-7).AddHours(6)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, DayOfWeek.Monday)]
    [InlineData(6, DayOfWeek.Saturday)]
    [InlineData(7, DayOfWeek.Sunday)]
    public void Un_numero_ISO_valido_se_convierte_en_su_dia(int isoDay, DayOfWeek expected)
    {
        DoctorAvailabilitySeries.TryFromIsoDayNumber(isoDay, out var day).ShouldBeTrue();
        day.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(-1)]
    public void Un_numero_ISO_fuera_de_1_a_7_no_es_un_dia(int isoDay)
    {
        DoctorAvailabilitySeries.TryFromIsoDayNumber(isoDay, out _).ShouldBeFalse();
    }

    private static DoctorAvailabilitySeries Series(
        DayOfWeek[] days, TimeOnly start, TimeOnly end, DateOnly? startDate = null, DateOnly? endDate = null) =>
        DoctorAvailabilitySeriesMother.Create(days, start, end, startDate, endDate).Value;
}
