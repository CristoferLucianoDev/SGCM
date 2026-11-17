using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling;

public sealed class HourAlignedInstantTests
{
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);

    [Fact]
    public void Un_instante_en_punto_se_crea_y_se_guarda_en_UTC()
    {
        var result = HourAlignedInstant.Create(new DateTimeOffset(2027, 1, 4, 8, 0, 0, SantoDomingo));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.Zero));
        result.Value.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(30, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void Un_instante_con_minutos_segundos_o_milisegundos_no_esta_en_punto(int minutes, int seconds, int milliseconds)
    {
        var instant = new DateTimeOffset(2027, 1, 4, 8, minutes, seconds, milliseconds, SantoDomingo);

        HourAlignedInstant.IsOnHour(instant).ShouldBeFalse();
        var result = HourAlignedInstant.Create(instant);
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SchedulingErrors.NotOnHour);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void En_punto_se_mide_en_UTC_como_en_la_base()
    {
        // 8:00 en la India (UTC+5:30) son las 2:30 UTC.
        HourAlignedInstant.IsOnHour(new DateTimeOffset(2027, 1, 4, 8, 0, 0, TimeSpan.FromHours(5.5))).ShouldBeFalse();
        HourAlignedInstant.IsOnHour(new DateTimeOffset(2027, 1, 4, 8, 30, 0, TimeSpan.FromHours(5.5))).ShouldBeTrue();
    }

    [Theory]
    [InlineData(8, 0, 0, 0, true)]
    [InlineData(0, 0, 0, 0, true)]
    [InlineData(8, 30, 0, 0, false)]
    [InlineData(8, 0, 1, 0, false)]
    [InlineData(8, 0, 0, 1, false)]
    public void Una_hora_del_dia_esta_en_punto_solo_sin_minutos_ni_segundos(
        int hour, int minute, int second, int millisecond, bool expected)
    {
        HourAlignedInstant.IsOnHour(new TimeOnly(hour, minute, second, millisecond)).ShouldBe(expected);
    }

    [Fact]
    public void Dos_instantes_iguales_en_distinto_desfase_son_el_mismo_valor()
    {
        var local = HourAlignedInstant.Create(new DateTimeOffset(2027, 1, 4, 8, 0, 0, SantoDomingo)).Value;
        var utc = HourAlignedInstant.Create(new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.Zero)).Value;

        local.ShouldBe(utc);
        ((DateTimeOffset)local).ShouldBe(utc.Value);
    }
}
