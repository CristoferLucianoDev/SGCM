using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling;

public sealed class WeeklyRecurrenceTests
{
    private const int MinimumHours = 6;

    // Lunes 4 de enero de 2027.
    private static readonly DateOnly Monday = new(2027, 1, 4);
    private static readonly DayOfWeek[] MondayWednesdayFriday = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];

    [Fact]
    public void Una_recurrencia_valida_conserva_sus_datos_con_los_dias_ordenados_y_sin_repetir()
    {
        var result = WeeklyRecurrence.Create(
            [DayOfWeek.Sunday, DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Friday],
            new TimeOnly(8, 0), new TimeOnly(14, 0), Monday, Monday.AddMonths(6), MinimumHours);

        result.IsSuccess.ShouldBeTrue();
        var recurrence = result.Value;
        recurrence.DaysOfWeek.ShouldBe(new[] { DayOfWeek.Monday, DayOfWeek.Friday, DayOfWeek.Sunday });
        recurrence.StartTime.ShouldBe(new TimeOnly(8, 0));
        recurrence.EndTime.ShouldBe(new TimeOnly(14, 0));
        recurrence.Duration.ShouldBe(TimeSpan.FromHours(6));
        recurrence.StartDate.ShouldBe(Monday);
        recurrence.EndDate.ShouldBe(Monday.AddMonths(6));
    }

    [Fact]
    public void Los_dias_de_la_semana_no_pueden_estar_vacios()
    {
        AssertRejected(Create(days: []), WeeklyRecurrenceErrors.DaysRequired);
        AssertRejected(WeeklyRecurrence.Create(null, new TimeOnly(8, 0), new TimeOnly(14, 0), Monday, null, MinimumHours),
            WeeklyRecurrenceErrors.DaysRequired);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void Un_dia_fuera_de_la_semana_se_rechaza(int day) =>
        AssertRejected(Create(days: [DayOfWeek.Monday, (DayOfWeek)day]), WeeklyRecurrenceErrors.InvalidDayOfWeek);

    [Theory]
    [InlineData(8, 30, 14, 0)]
    [InlineData(8, 0, 14, 15)]
    public void Las_horas_van_en_punto(int startHour, int startMinute, int endHour, int endMinute) =>
        AssertRejected(Create(new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute)),
            WeeklyRecurrenceErrors.TimeNotOnHour);

    [Theory]
    [InlineData(14, 8)]
    [InlineData(10, 10)]
    public void El_fin_debe_ser_posterior_al_inicio(int start, int end) =>
        AssertRejected(Create(new TimeOnly(start, 0), new TimeOnly(end, 0)), WeeklyRecurrenceErrors.InvalidTimeRange);

    [Fact]
    public void La_duracion_minima_es_la_de_la_configuracion()
    {
        AssertRejected(Create(new TimeOnly(8, 0), new TimeOnly(13, 0)), WeeklyRecurrenceErrors.DurationTooShort);
        Create(new TimeOnly(8, 0), new TimeOnly(14, 0)).IsSuccess.ShouldBeTrue();
        AssertRejected(Create(new TimeOnly(8, 0), new TimeOnly(14, 0), minimumHours: 8), WeeklyRecurrenceErrors.DurationTooShort);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_duracion_minima_no_positiva_es_un_error_de_programacion(int hours)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Create(minimumHours: hours));
    }

    [Fact]
    public void La_fecha_hasta_si_existe_no_puede_ser_anterior_a_la_fecha_desde()
    {
        AssertRejected(Create(endDate: Monday.AddDays(-1)), WeeklyRecurrenceErrors.InvalidDateRange);
        Create(endDate: Monday).IsSuccess.ShouldBeTrue();
        Create(endDate: null).Value.EndDate.ShouldBeNull();
    }

    [Fact]
    public void Da_los_dias_en_que_se_repite_entre_dos_fechas_recortados_a_su_vigencia()
    {
        var recurrence = Create(endDate: Monday.AddDays(9)).Value;

        recurrence.DatesBetween(Monday.AddDays(-7), Monday.AddDays(30))
            .ShouldBe(new[] { Monday, Monday.AddDays(2), Monday.AddDays(4), Monday.AddDays(7), Monday.AddDays(9) });
        recurrence.DatesBetween(Monday.AddDays(1), Monday.AddDays(1)).ShouldBeEmpty();
        recurrence.DatesBetween(Monday.AddDays(5), Monday).ShouldBeEmpty();
    }

    [Fact]
    public void Dos_recurrencias_con_un_dia_en_comun_y_horas_que_se_cruzan_se_traslapan()
    {
        var other = Create(new TimeOnly(10, 0), new TimeOnly(16, 0), days: [DayOfWeek.Monday]).Value;

        Create().Value.OverlapsWith(other).ShouldBeTrue();
        other.OverlapsWith(Create().Value).ShouldBeTrue();
    }

    [Fact]
    public void Dos_recurrencias_en_dias_distintos_no_se_traslapan()
    {
        Create().Value.OverlapsWith(Create(days: [DayOfWeek.Tuesday, DayOfWeek.Thursday]).Value).ShouldBeFalse();
    }

    [Fact]
    public void Dos_horarios_contiguos_el_mismo_dia_no_se_traslapan()
    {
        Create().Value.OverlapsWith(Create(new TimeOnly(14, 0), new TimeOnly(20, 0)).Value).ShouldBeFalse();
    }

    [Fact]
    public void Una_recurrencia_que_termina_antes_de_que_empiece_la_otra_no_se_traslapa()
    {
        var earlier = Create(startDate: Monday.AddDays(-30), endDate: Monday.AddDays(-1)).Value;

        Create().Value.OverlapsWith(earlier).ShouldBeFalse();
    }

    [Fact]
    public void Un_tramo_comun_corto_sin_el_dia_compartido_no_se_traslapa()
    {
        // Ambas incluyen el viernes, pero solo comparten del lunes 4 al miércoles 6.
        var recurrence = Create(endDate: Monday.AddDays(2)).Value;
        var other = Create(days: [DayOfWeek.Friday], startDate: Monday.AddDays(-14), endDate: Monday.AddDays(2)).Value;

        recurrence.OverlapsWith(other).ShouldBeFalse();
    }

    [Fact]
    public void Sin_fecha_de_fin_detecta_el_traslape_aunque_una_empiece_meses_despues()
    {
        var later = Create(new TimeOnly(13, 0), new TimeOnly(19, 0), days: [DayOfWeek.Friday], startDate: Monday.AddMonths(6)).Value;

        Create().Value.OverlapsWith(later).ShouldBeTrue();
        later.OverlapsWith(Create().Value).ShouldBeTrue();
    }

    [Fact]
    public void Sin_fecha_de_fin_dos_recurrencias_en_dias_distintos_nunca_se_traslapan()
    {
        var weekend = Create(days: [DayOfWeek.Saturday, DayOfWeek.Sunday], startDate: Monday.AddYears(1)).Value;

        Create().Value.OverlapsWith(weekend).ShouldBeFalse();
    }

    private static Result<WeeklyRecurrence> Create(
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        DayOfWeek[]? days = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int minimumHours = MinimumHours) =>
        WeeklyRecurrence.Create(
            days ?? MondayWednesdayFriday,
            startTime ?? new TimeOnly(8, 0),
            endTime ?? new TimeOnly(14, 0),
            startDate ?? Monday,
            endDate,
            minimumHours);

    private static void AssertRejected(Result<WeeklyRecurrence> result, Error expected)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(expected);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }
}
