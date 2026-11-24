using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling;

public sealed class TimeRangeTests
{
    private static readonly DateTimeOffset Eight = new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Un_rango_con_el_fin_despues_del_inicio_se_crea_en_UTC_con_su_duracion()
    {
        var result = TimeRange.Create(
            new DateTimeOffset(2027, 1, 4, 8, 0, 0, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2027, 1, 4, 14, 0, 0, TimeSpan.FromHours(-4)));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Start.ShouldBe(Eight);
        result.Value.Start.Offset.ShouldBe(TimeSpan.Zero);
        result.Value.End.ShouldBe(Eight.AddHours(6));
        result.Value.End.Offset.ShouldBe(TimeSpan.Zero);
        result.Value.Duration.ShouldBe(TimeSpan.FromHours(6));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_fin_igual_o_anterior_al_inicio_se_rechaza(int hours)
    {
        TimeRange.IsValid(Eight, Eight.AddHours(hours)).ShouldBeFalse();
        var result = TimeRange.Create(Eight, Eight.AddHours(hours));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SchedulingErrors.InvalidTimeRange);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData(2, 8, true)]
    [InlineData(-2, 1, true)]
    [InlineData(1, 3, true)]
    [InlineData(-1, 7, true)]
    [InlineData(6, 12, false)]
    [InlineData(-6, 0, false)]
    [InlineData(24, 30, false)]
    public void Detecta_el_traslape_y_dos_rangos_contiguos_no_se_traslapan(int startHour, int endHour, bool expected)
    {
        var range = Range(0, 6);
        var other = Range(startHour, endHour);

        range.Overlaps(other).ShouldBe(expected);
        other.Overlaps(range).ShouldBe(expected);
        range.Overlaps(other.Start, other.End).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 2, true)]
    [InlineData(4, 6, true)]
    [InlineData(0, 6, true)]
    [InlineData(5, 7, false)]
    [InlineData(-1, 1, false)]
    public void Contiene_un_rango_solo_si_cabe_completo(int startHour, int endHour, bool expected)
    {
        Range(0, 6).Contains(Range(startHour, endHour)).ShouldBe(expected);
    }

    [Fact]
    public void Dos_rangos_con_los_mismos_instantes_son_el_mismo_valor()
    {
        var utc = Range(0, 6);
        var local = TimeRange.Create(Eight.ToOffset(TimeSpan.FromHours(-4)), Eight.AddHours(6).ToOffset(TimeSpan.FromHours(-4))).Value;

        local.ShouldBe(utc);
    }

    private static TimeRange Range(int startHour, int endHour) =>
        TimeRange.Create(Eight.AddHours(startHour), Eight.AddHours(endHour)).Value;
}

public sealed class TimeRangeSubtractTests
{
    private static readonly DateTimeOffset Start = new(2027, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly SGCM.Data.Domain.Scheduling.TimeRange Block = At(0, 6);

    [Fact]
    public void Sin_cruce_queda_el_rango_completo() => Block.Subtract(At(6, 8)).ShouldBe(new[] { Block });

    [Fact]
    public void Cubierto_completo_no_queda_nada() => Block.Subtract(At(-1, 7)).ShouldBeEmpty();

    [Fact]
    public void Cruzando_el_inicio_queda_el_final() => Block.Subtract(At(-1, 2)).ShouldBe(new[] { At(2, 6) });

    [Fact]
    public void Cruzando_el_fin_queda_el_principio() => Block.Subtract(At(4, 8)).ShouldBe(new[] { At(0, 4) });

    [Fact]
    public void En_medio_quedan_dos_partes_en_orden() => Block.Subtract(At(2, 4)).ShouldBe(new[] { At(0, 2), At(4, 6) });

    private static SGCM.Data.Domain.Scheduling.TimeRange At(int fromHour, int toHour) =>
        SGCM.Data.Domain.Scheduling.TimeRange.Create(Start.AddHours(fromHour), Start.AddHours(toHour)).Value;
}
