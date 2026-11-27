using SGCM.Data.Application.Scheduling.Commands.RescheduleAppointment;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class RescheduleAppointmentCommandValidatorTests
{
    private const int AppointmentId = 99;

    private static readonly DateTimeOffset StartsAt = new(2027, 1, 20, 13, 0, 0, TimeSpan.Zero);

    private readonly RescheduleAppointmentCommandValidator _validator = new();

    [Fact]
    public void Un_comando_con_id_positivo_y_nueva_hora_es_valido()
    {
        _validator.Validate(new RescheduleAppointmentCommand(AppointmentId, StartsAt)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_id_de_la_cita_debe_ser_positivo(int appointmentId)
    {
        AssertSingleError(new RescheduleAppointmentCommand(appointmentId, StartsAt), "Appointment.InvalidAppointmentId");
    }

    [Fact]
    public void La_nueva_hora_es_obligatoria()
    {
        AssertSingleError(new RescheduleAppointmentCommand(AppointmentId, null), "Appointment.StartsAtRequired");
    }

    private void AssertSingleError(RescheduleAppointmentCommand command, string code)
    {
        var result = _validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
    }
}
