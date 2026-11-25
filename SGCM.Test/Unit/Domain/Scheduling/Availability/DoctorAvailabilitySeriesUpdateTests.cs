using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

public sealed class DoctorAvailabilitySeriesUpdateTests
{
    private const int MinimumHours = DoctorAvailabilitySeriesMother.MinimumDurationHours;
    private static readonly DayOfWeek[] TuesdayAndThursday = [DayOfWeek.Tuesday, DayOfWeek.Thursday];
    private static readonly DateOnly FirstEditable = new(2027, 1, 18);

    [Fact]
    public void Cambia_la_recurrencia_y_la_serie_empieza_en_la_primera_fecha_editable()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();

        var result = series.Update(TuesdayAndThursday, new TimeOnly(10, 0), new TimeOnly(16, 0), new DateOnly(2027, 6, 30), FirstEditable, MinimumHours);

        result.IsSuccess.ShouldBeTrue();
        series.DaysOfWeek.ShouldBe(TuesdayAndThursday);
        series.StartTime.ShouldBe(new TimeOnly(10, 0));
        series.EndTime.ShouldBe(new TimeOnly(16, 0));
        series.StartDate.ShouldBe(FirstEditable);
        series.EndDate.ShouldBe(new DateOnly(2027, 6, 30));
        series.TimeZone.ShouldBe(DoctorAvailabilitySeriesMother.TimeZone);
    }

    [Fact]
    public void Si_la_serie_empieza_despues_de_la_primera_fecha_editable_conserva_su_fecha_desde()
    {
        var later = new DateOnly(2027, 2, 1);
        var series = DoctorAvailabilitySeriesMother.Create(startDate: later).Value;

        series.Update(TuesdayAndThursday, new TimeOnly(10, 0), new TimeOnly(16, 0), null, FirstEditable, MinimumHours).IsSuccess.ShouldBeTrue();

        series.StartDate.ShouldBe(later);
        series.EndDate.ShouldBeNull();
    }

    [Fact]
    public void Una_fecha_hasta_anterior_a_la_primera_fecha_editable_se_rechaza_sin_cambiar_la_serie()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();

        var result = series.Update(TuesdayAndThursday, new TimeOnly(10, 0), new TimeOnly(16, 0), FirstEditable.AddDays(-1), FirstEditable, MinimumHours);

        result.Error.ShouldBe(DoctorAvailabilitySeriesErrors.InvalidDateRange);
        AssertUnchanged(series);
    }

    [Fact]
    public void Usa_los_errores_de_la_serie_para_un_patron_invalido()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();

        series.Update([], new TimeOnly(10, 0), new TimeOnly(16, 0), null, FirstEditable, MinimumHours).Error
            .ShouldBe(DoctorAvailabilitySeriesErrors.DaysRequired);
        series.Update(TuesdayAndThursday, new TimeOnly(10, 30), new TimeOnly(16, 0), null, FirstEditable, MinimumHours).Error
            .ShouldBe(DoctorAvailabilitySeriesErrors.TimeNotOnHour);
        series.Update(TuesdayAndThursday, new TimeOnly(10, 0), new TimeOnly(15, 0), null, FirstEditable, MinimumHours).Error
            .ShouldBe(DoctorAvailabilitySeriesErrors.DurationTooShort);
        AssertUnchanged(series);
    }

    [Fact]
    public void Una_serie_inactiva_no_se_edita()
    {
        var series = DoctorAvailabilitySeriesMother.Valid();
        series.Deactivate();

        series.Update(TuesdayAndThursday, new TimeOnly(10, 0), new TimeOnly(16, 0), null, FirstEditable, MinimumHours).Error
            .ShouldBe(DoctorAvailabilitySeriesErrors.NotFound);
        AssertUnchanged(series);
    }

    private static void AssertUnchanged(DoctorAvailabilitySeries series)
    {
        series.DaysOfWeek.ShouldBe(DoctorAvailabilitySeriesMother.DaysOfWeek);
        series.StartTime.ShouldBe(DoctorAvailabilitySeriesMother.StartTime);
        series.EndTime.ShouldBe(DoctorAvailabilitySeriesMother.EndTime);
        series.StartDate.ShouldBe(DoctorAvailabilitySeriesMother.StartDate);
        series.EndDate.ShouldBeNull();
    }
}
