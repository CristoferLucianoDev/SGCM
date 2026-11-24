using Microsoft.Extensions.Options;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.RescheduleAvailabilityBlock;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class RescheduleAvailabilityBlockCommandValidatorTests
{
    private static readonly RescheduleAvailabilityBlockCommand Valid =
        new(40, new DateOnly(2027, 1, 20), new TimeOnly(10, 0), new TimeOnly(16, 0));

    private readonly RescheduleAvailabilityBlockCommandValidator _validator = new(
        BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone),
        Options.Create(new SchedulingOptions { MinimumBlockDurationHours = 6 }));

    [Fact]
    public void Un_horario_valido_con_o_sin_motivo_es_valido()
    {
        _validator.Validate(Valid).IsValid.ShouldBeTrue();
        _validator.Validate(Valid with { Reason = "Cambio de consultorio" }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void El_bloque_debe_ser_positivo()
    {
        AssertSingleError(Valid with { BlockId = 0 }, "Availability.InvalidBlockId", nameof(RescheduleAvailabilityBlockCommand.BlockId));
    }

    [Fact]
    public void El_inicio_debe_estar_en_punto()
    {
        AssertSingleError(Valid with { StartTime = new TimeOnly(10, 30) }, "Availability.NotOnHour", nameof(RescheduleAvailabilityBlockCommand.StartTime));
    }

    [Fact]
    public void El_fin_debe_ser_posterior_al_inicio()
    {
        AssertSingleError(Valid with { EndTime = new TimeOnly(9, 0) }, "Availability.InvalidTimeRange", nameof(RescheduleAvailabilityBlockCommand.EndTime));
    }

    [Fact]
    public void El_horario_nuevo_respeta_la_duracion_minima()
    {
        AssertSingleError(Valid with { EndTime = new TimeOnly(15, 0) }, "Availability.DurationTooShort", nameof(RescheduleAvailabilityBlockCommand.EndTime));
    }

    [Fact]
    public void Un_motivo_de_500_caracteres_es_valido_y_uno_de_501_no()
    {
        _validator.Validate(Valid with { Reason = new string('A', StatusReason.MaxLength) }).IsValid.ShouldBeTrue();
        AssertSingleError(Valid with { Reason = new string('A', StatusReason.MaxLength + 1) },
            AppointmentErrors.StatusReasonTooLong.Code, nameof(RescheduleAvailabilityBlockCommand.Reason));
    }

    private void AssertSingleError(RescheduleAvailabilityBlockCommand command, string code, string property)
    {
        var error = _validator.Validate(command).Errors.ShouldHaveSingleItem();
        error.ErrorCode.ShouldBe(code);
        error.PropertyName.ShouldBe(property);
    }
}
