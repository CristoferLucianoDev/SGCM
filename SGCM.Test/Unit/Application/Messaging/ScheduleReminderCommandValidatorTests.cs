using SGCM.Data.Application.Messaging.Commands.ScheduleReminder;
using Shouldly;

namespace SGCM.Test.Unit.Application.Messaging;

/// <summary>Pruebas de <see cref="ScheduleReminderCommandValidator"/>. El texto y la hora futura los valida el agregado.</summary>
public sealed class ScheduleReminderCommandValidatorTests
{
    private static readonly DateTimeOffset SendAt = new(2027, 1, 11, 9, 0, 0, TimeSpan.FromHours(-4));
    private static readonly ScheduleReminderCommand Valid = new(99, 1, "Hola Ana", SendAt);

    private readonly ScheduleReminderCommandValidator _validator = new();

    [Fact]
    public void Acepta_un_recordatorio_con_plantilla_o_libre()
    {
        _validator.Validate(Valid).IsValid.ShouldBeTrue();
        _validator.Validate(Valid with { TemplateId = null }).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_id_de_la_cita_debe_ser_mayor_que_cero(int appointmentId)
    {
        AssertSingleError(Valid with { AppointmentId = appointmentId }, "Appointment.InvalidAppointmentId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_id_de_la_plantilla_si_se_indica_debe_ser_mayor_que_cero(int templateId)
    {
        AssertSingleError(Valid with { TemplateId = templateId }, "ReminderTemplate.InvalidId");
    }

    [Fact]
    public void La_fecha_y_hora_de_envio_es_obligatoria()
    {
        AssertSingleError(Valid with { ScheduledAt = null }, "Reminder.ScheduledAtRequired");
    }

    private void AssertSingleError(ScheduleReminderCommand command, string code)
    {
        var result = _validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
    }
}
