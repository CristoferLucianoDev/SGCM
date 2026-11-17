using Microsoft.Extensions.Options;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;
using SGCM.Data.Domain.Scheduling;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class CreateAvailabilityBlockCommandValidatorTests
{
    private readonly CreateAvailabilityBlockCommandValidator _validator = new(
        BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone),
        Options.Create(CreateAvailabilitySeriesCommandMother.Options()));

    [Fact]
    public void Un_comando_valido_pasa()
    {
        _validator.Validate(CreateAvailabilityBlockCommandMother.Valid()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Una_fecha_pasada_no_la_valida_el_validador_sino_el_handler()
    {
        _validator.Validate(CreateAvailabilityBlockCommandMother.Valid() with { Date = new DateOnly(2000, 1, 3) })
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Una_hora_que_no_esta_en_punto_se_rechaza_en_StartTime() =>
        AssertFails(CreateAvailabilityBlockCommandMother.Valid() with { StartTime = new TimeOnly(8, 30) },
            "Availability.NotOnHour", nameof(CreateAvailabilityBlockCommand.StartTime));

    [Fact]
    public void Un_fin_anterior_al_inicio_se_rechaza_en_EndTime() =>
        AssertFails(
            CreateAvailabilityBlockCommandMother.Valid() with { StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(8, 0) },
            "Availability.InvalidTimeRange", nameof(CreateAvailabilityBlockCommand.EndTime));

    [Fact]
    public void Menos_de_la_duracion_minima_se_rechaza_en_EndTime() =>
        AssertFails(CreateAvailabilityBlockCommandMother.Valid() with { EndTime = new TimeOnly(13, 0) },
            "Availability.DurationTooShort", nameof(CreateAvailabilityBlockCommand.EndTime));

    private void AssertFails(CreateAvailabilityBlockCommand command, string code, string property)
    {
        var failure = _validator.Validate(command).Errors.ShouldHaveSingleItem();

        failure.ErrorCode.ShouldBe(code);
        failure.PropertyName.ShouldBe(property);
    }
}
