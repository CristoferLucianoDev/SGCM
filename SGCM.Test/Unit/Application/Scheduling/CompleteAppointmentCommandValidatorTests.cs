using SGCM.Data.Application.Scheduling.Commands.CompleteAppointment;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class CompleteAppointmentCommandValidatorTests
{
    private const int AppointmentId = 99;

    private readonly CompleteAppointmentCommandValidator _validator = new();

    [Fact]
    public void Un_comando_con_id_positivo_es_valido()
    {
        _validator.Validate(new CompleteAppointmentCommand(AppointmentId)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_id_de_la_cita_debe_ser_positivo(int appointmentId)
    {
        var result = _validator.Validate(new CompleteAppointmentCommand(appointmentId));

        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("Appointment.InvalidAppointmentId");
    }
}
