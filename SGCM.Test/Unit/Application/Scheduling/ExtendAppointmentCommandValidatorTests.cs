using SGCM.Data.Application.Scheduling.Commands.ExtendAppointment;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class ExtendAppointmentCommandValidatorTests
{
    private const int AppointmentId = 99;
    private const int OneHour = 1;

    private readonly ExtendAppointmentCommandValidator _validator = new();

    [Theory]
    [InlineData(OneHour)]
    [InlineData(ExtendAppointmentCommandValidator.MaximumAdditionalHours)]
    public void Un_comando_con_id_positivo_y_horas_dentro_del_rango_es_valido(int additionalHours)
    {
        _validator.Validate(new ExtendAppointmentCommand(AppointmentId, additionalHours)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_id_de_la_cita_debe_ser_positivo(int appointmentId)
    {
        var result = _validator.Validate(new ExtendAppointmentCommand(appointmentId, OneHour));

        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("Appointment.InvalidAppointmentId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ExtendAppointmentCommandValidator.MaximumAdditionalHours + 1)]
    public void Las_horas_fuera_del_rango_se_rechazan(int additionalHours)
    {
        var result = _validator.Validate(new ExtendAppointmentCommand(AppointmentId, additionalHours));

        var error = result.Errors.ShouldHaveSingleItem();
        error.ErrorCode.ShouldBe("Appointment.InvalidAdditionalHours");
        error.ErrorMessage.ShouldBe(
            $"Las horas a sumar deben estar entre 1 y {ExtendAppointmentCommandValidator.MaximumAdditionalHours}.");
    }
}
