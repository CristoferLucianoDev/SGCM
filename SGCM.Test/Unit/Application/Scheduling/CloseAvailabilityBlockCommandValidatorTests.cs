using SGCM.Data.Application.Scheduling.Commands.CloseAvailabilityBlock;
using SGCM.Data.Domain.Scheduling.Appointments;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class CloseAvailabilityBlockCommandValidatorTests
{
    private readonly CloseAvailabilityBlockCommandValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("Congreso médico")]
    public void Un_bloque_positivo_con_o_sin_motivo_es_valido(string? reason)
    {
        _validator.Validate(new CloseAvailabilityBlockCommand(40, reason)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_bloque_debe_ser_positivo(int blockId)
    {
        _validator.Validate(new CloseAvailabilityBlockCommand(blockId)).Errors.ShouldHaveSingleItem()
            .ErrorCode.ShouldBe("Availability.InvalidBlockId");
    }

    [Fact]
    public void Un_motivo_de_500_caracteres_es_valido_y_uno_de_501_no()
    {
        _validator.Validate(new CloseAvailabilityBlockCommand(40, new string('A', StatusReason.MaxLength))).IsValid.ShouldBeTrue();
        _validator.Validate(new CloseAvailabilityBlockCommand(40, new string('A', StatusReason.MaxLength + 1)))
            .Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(AppointmentErrors.StatusReasonTooLong.Code);
    }
}
