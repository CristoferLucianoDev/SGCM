using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling;

public sealed class BusinessCalendarTests
{
    private readonly BusinessCalendar _calendar = BusinessCalendar.ForTimeZone("America/Santo_Domingo");

    [Fact]
    public void Se_crea_con_una_zona_valida_y_recorta_los_espacios()
    {
        var result = BusinessCalendar.Create("  America/Santo_Domingo  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.TimeZoneId.ShouldBe("America/Santo_Domingo");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Zona/Inexistente")]
    public void Una_zona_invalida_se_rechaza(string? timeZoneId)
    {
        var result = BusinessCalendar.Create(timeZoneId);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SchedulingErrors.InvalidTimeZone);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        Should.Throw<ArgumentException>(() => BusinessCalendar.ForTimeZone(timeZoneId!));
    }

    [Theory]
    [InlineData(2027, 1, 5, 3, 2027, 1, 4)]
    [InlineData(2027, 1, 5, 4, 2027, 1, 5)]
    [InlineData(2027, 1, 1, 0, 2026, 12, 31)]
    public void Hoy_es_la_fecha_local_y_no_la_fecha_UTC(
        int year, int month, int day, int hourUtc, int expectedYear, int expectedMonth, int expectedDay)
    {
        var utcNow = new DateTimeOffset(year, month, day, hourUtc, 0, 0, TimeSpan.Zero);

        _calendar.Today(utcNow).ShouldBe(new DateOnly(expectedYear, expectedMonth, expectedDay));
        _calendar.ToLocalDate(utcNow).ShouldBe(new DateOnly(expectedYear, expectedMonth, expectedDay));
    }

    [Fact]
    public void Convierte_un_instante_a_la_hora_local_con_su_desfase()
    {
        var local = _calendar.ToLocal(new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.Zero));

        local.Offset.ShouldBe(TimeSpan.FromHours(-4));
        local.Hour.ShouldBe(8);
    }

    [Fact]
    public void Convierte_una_fecha_y_hora_locales_a_UTC()
    {
        var utc = _calendar.ToUtc(new DateOnly(2027, 1, 4), new TimeOnly(8, 0));

        utc.ShouldBe(new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.Zero));
        utc.Offset.ShouldBe(TimeSpan.Zero);
        _calendar.TryToUtc(new DateOnly(2027, 1, 4), new TimeOnly(21, 0), out var night).ShouldBeTrue();
        night.ShouldBe(new DateTimeOffset(2027, 1, 5, 1, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Una_hora_local_que_no_existe_por_el_horario_de_verano_no_se_convierte()
    {
        // En Nueva York el reloj salta de 2:00 a 3:00 el 14 de marzo de 2027.
        var newYork = BusinessCalendar.ForTimeZone("America/New_York");

        newYork.TryToUtc(new DateOnly(2027, 3, 14), new TimeOnly(2, 0), out _).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => newYork.ToUtc(new DateOnly(2027, 3, 14), new TimeOnly(2, 0)));
        newYork.ToUtc(new DateOnly(2027, 3, 14), new TimeOnly(3, 0))
            .ShouldBe(new DateTimeOffset(2027, 3, 14, 7, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(4, DayOfWeek.Monday)]
    [InlineData(6, DayOfWeek.Wednesday)]
    [InlineData(10, DayOfWeek.Sunday)]
    public void Da_el_dia_de_la_semana_local(int day, DayOfWeek expected)
    {
        // A las 14:00 UTC en Santo Domingo son las 10:00 del mismo día.
        _calendar.DayOfWeekOf(new DateTimeOffset(2027, 1, day, 14, 0, 0, TimeSpan.Zero)).ShouldBe(expected);
    }

    [Fact]
    public void El_dia_de_la_semana_tambien_se_cuenta_en_la_hora_local()
    {
        // Martes 5 de enero a las 02:00 UTC todavía es lunes 4 a las 22:00 en Santo Domingo.
        _calendar.DayOfWeekOf(new DateTimeOffset(2027, 1, 5, 2, 0, 0, TimeSpan.Zero)).ShouldBe(DayOfWeek.Monday);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(10)]
    public void La_semana_actual_empieza_el_lunes_y_la_siguiente_siete_dias_despues(int day)
    {
        // Del lunes 4 al domingo 10 de enero de 2027.
        var utcNow = new DateTimeOffset(2027, 1, day, 14, 0, 0, TimeSpan.Zero);

        _calendar.StartOfCurrentWeek(utcNow).ShouldBe(new DateOnly(2027, 1, 4));
        _calendar.StartOfNextWeek(utcNow).ShouldBe(new DateOnly(2027, 1, 11));
    }

    [Fact]
    public void La_semana_se_decide_con_la_fecha_local()
    {
        // Lunes 11 a las 02:00 UTC todavía es domingo 10 en Santo Domingo: sigue siendo la semana del 4.
        var utcNow = new DateTimeOffset(2027, 1, 11, 2, 0, 0, TimeSpan.Zero);

        _calendar.StartOfCurrentWeek(utcNow).ShouldBe(new DateOnly(2027, 1, 4));
        _calendar.StartOfNextWeek(utcNow).ShouldBe(new DateOnly(2027, 1, 11));
    }

    [Fact]
    public void La_semana_cruza_el_cambio_de_anio()
    {
        // Viernes 1 de enero de 2027: la semana empezó el lunes 28 de diciembre de 2026.
        var utcNow = new DateTimeOffset(2027, 1, 1, 14, 0, 0, TimeSpan.Zero);

        _calendar.StartOfCurrentWeek(utcNow).ShouldBe(new DateOnly(2026, 12, 28));
        _calendar.StartOfNextWeek(utcNow).ShouldBe(new DateOnly(2027, 1, 4));
    }
}
