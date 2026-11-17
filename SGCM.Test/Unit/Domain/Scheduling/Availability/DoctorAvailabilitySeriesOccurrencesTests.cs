using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

public sealed class DoctorAvailabilitySeriesOccurrencesTests
{
    // El lunes 4 de enero de 2027 empieza la serie del Mother.
    private static readonly DateOnly Monday = DoctorAvailabilitySeriesMother.StartDate;

    [Fact]
    public void Devuelve_un_bloque_por_cada_dia_de_la_serie_con_las_horas_en_UTC()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();

        var occurrences = series.GetOccurrences(Monday, Monday.AddDays(6));

        occurrences.Select(o => o.Date).ShouldBe(new[] { Monday, Monday.AddDays(2), Monday.AddDays(4) });
        // Santo Domingo está en UTC-4 todo el año: 8:00 a 14:00 local son 12:00 a 18:00 UTC.
        var first = occurrences[0];
        first.StartsAt.ShouldBe(new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.Zero));
        first.EndsAt.ShouldBe(new DateTimeOffset(2027, 1, 4, 18, 0, 0, TimeSpan.Zero));
        occurrences.ShouldAllBe(o => o.StartsAt.Offset == TimeSpan.Zero && o.EndsAt.Offset == TimeSpan.Zero);
    }

    [Fact]
    public void Ambas_fechas_del_rango_estan_incluidas()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();

        series.GetOccurrences(Monday.AddDays(2), Monday.AddDays(2)).ShouldHaveSingleItem().Date
            .ShouldBe(Monday.AddDays(2));
        series.GetOccurrences(Monday.AddDays(1), Monday.AddDays(4)).Select(o => o.Date)
            .ShouldBe(new[] { Monday.AddDays(2), Monday.AddDays(4) });
    }

    [Fact]
    public void No_genera_dias_antes_de_la_fecha_desde()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();

        var occurrences = series.GetOccurrences(Monday.AddDays(-14), Monday.AddDays(2));

        occurrences.Select(o => o.Date).ShouldBe(new[] { Monday, Monday.AddDays(2) });
    }

    [Fact]
    public void No_genera_dias_despues_de_la_fecha_hasta()
    {
        var series = DoctorAvailabilitySeriesMother.Valid(endDate: Monday.AddDays(2));

        var occurrences = series.GetOccurrences(Monday, Monday.AddDays(30));

        occurrences.Select(o => o.Date).ShouldBe(new[] { Monday, Monday.AddDays(2) });
    }

    [Fact]
    public void Un_rango_fuera_de_la_vigencia_o_invertido_no_genera_nada()
    {
        var series = DoctorAvailabilitySeriesMother.Valid(endDate: Monday.AddDays(13));

        series.GetOccurrences(Monday.AddDays(-30), Monday.AddDays(-1)).ShouldBeEmpty();
        series.GetOccurrences(Monday.AddDays(14), Monday.AddDays(40)).ShouldBeEmpty();
        series.GetOccurrences(Monday.AddDays(6), Monday).ShouldBeEmpty();
    }

    [Fact]
    public void Un_horizonte_de_90_dias_genera_los_dias_de_la_serie_en_orden()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();

        var occurrences = series.GetOccurrences(Monday, Monday.AddDays(89));

        // 12 semanas completas (36 días) más lunes a sábado de la siguiente (3 días).
        occurrences.Count.ShouldBe(39);
        occurrences.Select(o => o.Date).ShouldBeInOrder();
        occurrences.Select(o => o.Date).Distinct().Count().ShouldBe(39);
        occurrences.ShouldAllBe(o => o.Date.DayOfWeek == DayOfWeek.Monday
            || o.Date.DayOfWeek == DayOfWeek.Wednesday || o.Date.DayOfWeek == DayOfWeek.Friday);
        occurrences.ShouldAllBe(o => o.EndsAt - o.StartsAt == TimeSpan.FromHours(6));
    }

    [Fact]
    public void La_fecha_local_de_un_instante_se_cuenta_en_la_zona_de_la_serie()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();

        // 5 de enero a las 03:00 UTC todavía es 4 de enero a las 23:00 en Santo Domingo.
        series.LocalDateOf(new DateTimeOffset(2027, 1, 5, 3, 0, 0, TimeSpan.Zero)).ShouldBe(Monday);
        series.LocalDateOf(new DateTimeOffset(2027, 1, 5, 4, 0, 0, TimeSpan.Zero)).ShouldBe(Monday.AddDays(1));
    }

    [Fact]
    public void El_domingo_se_reconoce_como_dia_de_la_serie()
    {
        var series = DoctorAvailabilitySeriesMother.Valid([DayOfWeek.Sunday]);

        series.GetOccurrences(Monday, Monday.AddDays(6)).ShouldHaveSingleItem().Date
            .ShouldBe(new DateOnly(2027, 1, 10));
    }

    [Fact]
    public void Con_cambio_de_horario_cada_dia_usa_su_desfase_y_omite_la_hora_que_no_existe()
    {
        // En Nueva York el horario de verano empieza el domingo 14 de marzo de 2027 a las 2:00.
        var series = DoctorAvailabilitySeriesMother.Create(
            [DayOfWeek.Sunday], new TimeOnly(2, 0), new TimeOnly(8, 0), new DateOnly(2027, 3, 1),
            timeZone: "America/New_York").Value;

        var occurrences = series.GetOccurrences(new DateOnly(2027, 3, 7), new DateOnly(2027, 3, 21));

        occurrences.Select(o => o.Date).ShouldBe(new[] { new DateOnly(2027, 3, 7), new DateOnly(2027, 3, 21) });
        occurrences[0].StartsAt.ShouldBe(new DateTimeOffset(2027, 3, 7, 7, 0, 0, TimeSpan.Zero));
        occurrences[1].StartsAt.ShouldBe(new DateTimeOffset(2027, 3, 21, 6, 0, 0, TimeSpan.Zero));
    }
}
