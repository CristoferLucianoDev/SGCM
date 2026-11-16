using Microsoft.Extensions.Options;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class CreateAvailabilitySeriesCommandValidatorTests
{
    private readonly CreateAvailabilitySeriesCommandValidator _validator =
        new(Options.Create(CreateAvailabilitySeriesCommandMother.Options()));

    [Fact]
    public void Un_comando_valido_pasa()
    {
        _validator.Validate(CreateAvailabilitySeriesCommandMother.Valid()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Una_fecha_desde_pasada_no_la_valida_el_validador_sino_el_handler()
    {
        var command = CreateAvailabilitySeriesCommandMother.Valid() with { StartDate = new DateOnly(2000, 1, 3) };

        _validator.Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Sin_dias_se_rechaza_en_DaysOfWeek()
    {
        AssertFails(CreateAvailabilitySeriesCommandMother.Valid() with { DaysOfWeek = [] },
            "AvailabilitySeries.DaysRequired", nameof(CreateAvailabilitySeriesCommand.DaysOfWeek));
        AssertFails(CreateAvailabilitySeriesCommandMother.Valid() with { DaysOfWeek = null },
            "AvailabilitySeries.DaysRequired", nameof(CreateAvailabilitySeriesCommand.DaysOfWeek));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void Un_dia_fuera_de_1_a_7_se_rechaza(int isoDay) =>
        AssertFails(CreateAvailabilitySeriesCommandMother.Valid() with { DaysOfWeek = [1, isoDay] },
            "AvailabilitySeries.InvalidDayOfWeek", nameof(CreateAvailabilitySeriesCommand.DaysOfWeek));

    [Fact]
    public void Una_hora_que_no_esta_en_punto_se_rechaza() =>
        AssertFails(CreateAvailabilitySeriesCommandMother.Valid() with { StartTime = new TimeOnly(8, 30) },
            "AvailabilitySeries.TimeNotOnHour", nameof(CreateAvailabilitySeriesCommand.StartTime));

    [Fact]
    public void Un_fin_anterior_al_inicio_se_rechaza() =>
        AssertFails(
            CreateAvailabilitySeriesCommandMother.Valid() with { StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(8, 0) },
            "AvailabilitySeries.InvalidTimeRange", nameof(CreateAvailabilitySeriesCommand.EndTime));

    [Fact]
    public void Menos_de_la_duracion_minima_de_la_configuracion_se_rechaza()
    {
        AssertFails(CreateAvailabilitySeriesCommandMother.Valid() with { EndTime = new TimeOnly(13, 0) },
            "AvailabilitySeries.DurationTooShort", nameof(CreateAvailabilitySeriesCommand.EndTime));

        var options = CreateAvailabilitySeriesCommandMother.Options();
        options.MinimumBlockDurationHours = 8;
        new CreateAvailabilitySeriesCommandValidator(Options.Create(options))
            .Validate(CreateAvailabilitySeriesCommandMother.Valid()).Errors.ShouldHaveSingleItem()
            .ErrorCode.ShouldBe("AvailabilitySeries.DurationTooShort");
    }

    [Fact]
    public void Una_fecha_hasta_anterior_a_la_fecha_desde_se_rechaza() =>
        AssertFails(
            CreateAvailabilitySeriesCommandMother.Valid() with { EndDate = DoctorAvailabilitySeriesMother.StartDate.AddDays(-1) },
            "AvailabilitySeries.InvalidDateRange", nameof(CreateAvailabilitySeriesCommand.EndDate));

    private void AssertFails(CreateAvailabilitySeriesCommand command, string code, string property)
    {
        var failure = _validator.Validate(command).Errors.ShouldHaveSingleItem();

        failure.ErrorCode.ShouldBe(code);
        failure.PropertyName.ShouldBe(property);
    }
}
