using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling;

public sealed class RescheduleWeekRuleTests
{
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);
    private static readonly BusinessCalendar Calendar = BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone);

    // Semana actual: lunes 4 a domingo 10 de enero de 2027. Siguiente: 11 al 17. Posterior: desde el 18.
    private static readonly DateOnly CurrentMonday = new(2027, 1, 4);
    private static readonly DateOnly NextMonday = new(2027, 1, 11);
    private static readonly DateOnly WeekAfterMonday = new(2027, 1, 18);

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(10)]
    public void Un_horario_de_la_semana_actual_no_se_reprograma(int day)
    {
        RescheduleWeekRule.Check(new DateOnly(2027, 1, day), On(4), Calendar).ShouldBe(SchedulingErrors.CurrentWeekLocked);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void La_semana_siguiente_se_reprograma_de_lunes_a_miercoles(int today)
    {
        RescheduleWeekRule.Check(NextMonday, On(today), Calendar).ShouldBe(Error.None);
        RescheduleWeekRule.Check(NextMonday.AddDays(6), On(today), Calendar).ShouldBe(Error.None);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(10)]
    public void La_semana_siguiente_no_se_reprograma_de_jueves_a_domingo(int today)
    {
        RescheduleWeekRule.Check(NextMonday.AddDays(1), On(today), Calendar).ShouldBe(SchedulingErrors.NextWeekLocked);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(10)]
    public void Las_semanas_posteriores_se_reprograman_en_cualquier_momento(int today)
    {
        RescheduleWeekRule.Check(WeekAfterMonday, On(today), Calendar).ShouldBe(Error.None);
    }

    [Fact]
    public void El_dia_de_hoy_se_decide_en_la_hora_local()
    {
        // Miércoles 6 a las 23:30 en Santo Domingo ya es jueves en UTC, pero localmente sigue siendo miércoles.
        var lateWednesday = new DateTimeOffset(2027, 1, 6, 23, 30, 0, SantoDomingo);

        RescheduleWeekRule.Check(NextMonday, lateWednesday, Calendar).ShouldBe(Error.None);
    }

    [Theory]
    [InlineData(4, 11)]
    [InlineData(6, 11)]
    [InlineData(7, 18)]
    [InlineData(10, 18)]
    public void El_primer_dia_reprogramable_depende_del_dia_de_hoy(int today, int expectedDay)
    {
        RescheduleWeekRule.FirstReschedulableDate(On(today), Calendar).ShouldBe(new DateOnly(2027, 1, expectedDay));
    }

    [Fact]
    public void Un_calendario_nulo_es_un_error_de_programacion()
    {
        Should.Throw<ArgumentNullException>(() => RescheduleWeekRule.Check(CurrentMonday, On(4), null!));
        Should.Throw<ArgumentNullException>(() => RescheduleWeekRule.FirstReschedulableDate(On(4), null!));
    }

    private static DateTimeOffset On(int day) => new(2027, 1, day, 10, 0, 0, SantoDomingo);
}
