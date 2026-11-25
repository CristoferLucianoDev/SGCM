using Microsoft.Extensions.Options;
using SGCM.Data.Application.Scheduling.Commands.UpdateAvailabilitySeries;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class UpdateAvailabilitySeriesCommandValidatorTests
{
    private static readonly UpdateAvailabilitySeriesCommand Valid =
        new(15, [1, 2, 3], new TimeOnly(10, 0), new TimeOnly(16, 0), EndDate: null);

    private readonly UpdateAvailabilitySeriesCommandValidator _validator =
        new(Options.Create(CreateAvailabilitySeriesCommandMother.Options()));

    [Fact]
    public void Un_patron_valido_con_o_sin_fecha_hasta_y_motivo_es_valido()
    {
        _validator.Validate(Valid).IsValid.ShouldBeTrue();
        _validator.Validate(Valid with { EndDate = new DateOnly(2027, 6, 30), Reason = "Cambio de consultorio" }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void La_serie_debe_ser_positiva()
    {
        AssertSingleError(Valid with { SeriesId = 0 }, "AvailabilitySeries.InvalidSeriesId", nameof(UpdateAvailabilitySeriesCommand.SeriesId));
    }

    [Fact]
    public void Exige_al_menos_un_dia_valido()
    {
        AssertSingleError(Valid with { DaysOfWeek = [] }, "AvailabilitySeries.DaysRequired", nameof(UpdateAvailabilitySeriesCommand.DaysOfWeek));
        AssertSingleError(Valid with { DaysOfWeek = [8] }, "AvailabilitySeries.InvalidDayOfWeek", nameof(UpdateAvailabilitySeriesCommand.DaysOfWeek));
    }

    [Fact]
    public void Las_horas_van_en_punto_y_cumplen_la_duracion_minima()
    {
        AssertSingleError(Valid with { StartTime = new TimeOnly(10, 30) }, "AvailabilitySeries.TimeNotOnHour", nameof(UpdateAvailabilitySeriesCommand.StartTime));
        AssertSingleError(Valid with { EndTime = new TimeOnly(9, 0) }, "AvailabilitySeries.InvalidTimeRange", nameof(UpdateAvailabilitySeriesCommand.EndTime));
        AssertSingleError(Valid with { EndTime = new TimeOnly(15, 0) }, "AvailabilitySeries.DurationTooShort", nameof(UpdateAvailabilitySeriesCommand.EndTime));
    }

    [Fact]
    public void Un_motivo_de_500_caracteres_es_valido_y_uno_de_501_no()
    {
        _validator.Validate(Valid with { Reason = new string('A', StatusReason.MaxLength) }).IsValid.ShouldBeTrue();
        AssertSingleError(Valid with { Reason = new string('A', StatusReason.MaxLength + 1) },
            AppointmentErrors.StatusReasonTooLong.Code, nameof(UpdateAvailabilitySeriesCommand.Reason));
    }

    private void AssertSingleError(UpdateAvailabilitySeriesCommand command, string code, string property)
    {
        var error = _validator.Validate(command).Errors.ShouldHaveSingleItem();
        error.ErrorCode.ShouldBe(code);
        error.PropertyName.ShouldBe(property);
    }
}
