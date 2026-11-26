using SGCM.Data.Application.Scheduling.Commands.ConfirmAppointment;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class ConfirmAppointmentCommandValidatorTests
{
    private const int AppointmentId = 99;

    private readonly ConfirmAppointmentCommandValidator _validator = new();

    [Fact]
    public void Un_comando_con_id_positivo_es_valido()
    {
        _validator.Validate(new ConfirmAppointmentCommand(AppointmentId)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_id_de_la_cita_debe_ser_positivo(int appointmentId)
    {
        var result = _validator.Validate(new ConfirmAppointmentCommand(appointmentId));

        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("Appointment.InvalidAppointmentId");
    }
}
