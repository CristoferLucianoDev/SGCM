using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Domain.Scheduling.Availability.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

public sealed class DoctorAvailabilitySeriesCreateTests
{
    [Fact]
    public void Create_con_datos_validos_devuelve_una_serie_activa_con_sus_datos()
    {
        var result = DoctorAvailabilitySeriesMother.Create(endDate: new DateOnly(2027, 6, 30));

        result.IsSuccess.ShouldBeTrue();
        var series = result.Value;
        series.Id.ShouldBe(0);
        series.DoctorId.ShouldBe(DoctorAvailabilitySeriesMother.DoctorId);
        series.DaysOfWeek.ShouldBe(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday });
        series.StartTime.ShouldBe(new TimeOnly(8, 0));
        series.EndTime.ShouldBe(new TimeOnly(14, 0));
        series.StartDate.ShouldBe(DoctorAvailabilitySeriesMother.StartDate);
        series.EndDate.ShouldBe(new DateOnly(2027, 6, 30));
        series.TimeZone.ShouldBe(DoctorAvailabilitySeriesMother.TimeZone);
        series.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Create_sin_fecha_hasta_deja_la_serie_sin_fin()
    {
        var series = DoctorAvailabilitySeriesMother.Create().Value;

        series.EndDate.ShouldBeNull();
    }

    [Fact]
    public void Crear_la_serie_emite_el_evento_de_serie_creada_con_la_misma_serie()
    {
        var series = DoctorAvailabilitySeriesMother.Create().Value;

        var created = series.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DoctorAvailabilitySeriesCreatedEvent>();
        created.Series.ShouldBeSameAs(series);
    }

    [Fact]
    public void Create_quita_los_dias_repetidos_y_los_ordena_de_lunes_a_domingo()
    {
        var series = DoctorAvailabilitySeriesMother.Create(
            [DayOfWeek.Sunday, DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Friday]).Value;

        series.DaysOfWeek.ShouldBe(new[] { DayOfWeek.Monday, DayOfWeek.Friday, DayOfWeek.Sunday });
    }

    [Fact]
    public void Create_recorta_los_espacios_de_la_zona_horaria()
    {
        var series = DoctorAvailabilitySeriesMother.Create(timeZone: "  America/Santo_Domingo  ").Value;

        series.TimeZone.ShouldBe("America/Santo_Domingo");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_medico_no_positivo_se_rechaza(int doctorId) =>
        AssertRejected(DoctorAvailabilitySeriesMother.Create(doctorId: doctorId), DoctorAvailabilitySeriesErrors.InvalidDoctor);

    [Fact]
    public void Sin_dias_se_rechaza()
    {
        AssertRejected(DoctorAvailabilitySeriesMother.Create(daysOfWeek: []), DoctorAvailabilitySeriesErrors.DaysRequired);
        AssertRejected(
            DoctorAvailabilitySeries.Create(DoctorAvailabilitySeriesMother.DoctorId, null,
                DoctorAvailabilitySeriesMother.StartTime, DoctorAvailabilitySeriesMother.EndTime,
                DoctorAvailabilitySeriesMother.StartDate, null, DoctorAvailabilitySeriesMother.TimeZone,
                DoctorAvailabilitySeriesMother.MinimumDurationHours),
            DoctorAvailabilitySeriesErrors.DaysRequired);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void Un_dia_fuera_de_la_semana_se_rechaza(int day) =>
        AssertRejected(
            DoctorAvailabilitySeriesMother.Create([DayOfWeek.Monday, (DayOfWeek)day]),
            DoctorAvailabilitySeriesErrors.InvalidDayOfWeek);

    [Theory]
    [InlineData(8, 30, 0, 12, 0, 0)]
    [InlineData(8, 0, 1, 12, 0, 0)]
    [InlineData(8, 0, 0, 12, 15, 0)]
    [InlineData(8, 0, 0, 11, 59, 59)]
    public void Una_hora_que_no_esta_en_punto_se_rechaza(int sh, int sm, int ss, int eh, int em, int es) =>
        AssertRejected(
            DoctorAvailabilitySeriesMother.Create(startTime: new TimeOnly(sh, sm, ss), endTime: new TimeOnly(eh, em, es)),
            DoctorAvailabilitySeriesErrors.TimeNotOnHour);

    [Fact]
    public void Una_hora_con_milisegundos_no_esta_en_punto()
    {
        AssertRejected(
            DoctorAvailabilitySeriesMother.Create(startTime: new TimeOnly(8, 0, 0, 1)),
            DoctorAvailabilitySeriesErrors.TimeNotOnHour);
    }

    [Theory]
    [InlineData(12, 8)]
    [InlineData(10, 10)]
    public void Un_fin_anterior_o_igual_al_inicio_se_rechaza(int start, int end) =>
        AssertRejected(
            DoctorAvailabilitySeriesMother.Create(startTime: new TimeOnly(start, 0), endTime: new TimeOnly(end, 0)),
            DoctorAvailabilitySeriesErrors.InvalidTimeRange);

    [Fact]
    public void Un_horario_de_menos_de_6_horas_se_rechaza() =>
        AssertRejected(
            DoctorAvailabilitySeriesMother.Create(startTime: new TimeOnly(8, 0), endTime: new TimeOnly(13, 0)),
            DoctorAvailabilitySeriesErrors.DurationTooShort);

    [Fact]
    public void Un_horario_de_exactamente_6_horas_se_acepta()
    {
        var result = DoctorAvailabilitySeriesMother.Create(startTime: new TimeOnly(8, 0), endTime: new TimeOnly(14, 0));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void La_duracion_minima_viene_de_la_configuracion()
    {
        AssertRejected(
            DoctorAvailabilitySeriesMother.Create(minimumDurationHours: 8),
            DoctorAvailabilitySeriesErrors.DurationTooShort);
        DoctorAvailabilitySeriesMother.Create(endTime: new TimeOnly(16, 0), minimumDurationHours: 8)
            .IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Una_duracion_minima_no_positiva_es_un_error_de_programacion(int hours)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DoctorAvailabilitySeriesMother.Create(minimumDurationHours: hours));
    }

    [Fact]
    public void Una_fecha_hasta_anterior_a_la_fecha_desde_se_rechaza() =>
        AssertRejected(
            DoctorAvailabilitySeriesMother.Create(endDate: DoctorAvailabilitySeriesMother.StartDate.AddDays(-1)),
            DoctorAvailabilitySeriesErrors.InvalidDateRange);

    [Fact]
    public void Una_fecha_hasta_igual_a_la_fecha_desde_se_acepta()
    {
        var result = DoctorAvailabilitySeriesMother.Create(endDate: DoctorAvailabilitySeriesMother.StartDate);

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Zona/Inexistente")]
    public void Una_zona_horaria_invalida_se_rechaza(string? timeZone) =>
        AssertRejected(DoctorAvailabilitySeriesMother.Create(timeZone: timeZone), DoctorAvailabilitySeriesErrors.InvalidTimeZone);

    [Theory]
    [InlineData(DayOfWeek.Monday, 1)]
    [InlineData(DayOfWeek.Saturday, 6)]
    [InlineData(DayOfWeek.Sunday, 7)]
    public void El_numero_ISO_del_dia_es_el_que_guarda_la_base(DayOfWeek day, int expected)
    {
        DoctorAvailabilitySeries.IsoDayNumber(day).ShouldBe(expected);
    }

    private static void AssertRejected(Result<DoctorAvailabilitySeries> result, Error expected)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(expected);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }
}
