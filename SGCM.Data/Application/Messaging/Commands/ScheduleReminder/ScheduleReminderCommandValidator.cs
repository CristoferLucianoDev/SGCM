using FluentValidation;

namespace SGCM.Data.Application.Messaging.Commands.ScheduleReminder;

/// <summary>
/// Valida la forma de <see cref="ScheduleReminderCommand"/>: los identificadores deben ser mayores que cero y la hora
/// de envío es obligatoria. El texto y que la hora sea futura los valida el agregado.
/// </summary>
public sealed class ScheduleReminderCommandValidator : AbstractValidator<ScheduleReminderCommand>
{
    public ScheduleReminderCommandValidator()
    {
        RuleFor(command => command.AppointmentId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidAppointmentId")
            .WithMessage("El identificador de la cita debe ser mayor que cero.");

        RuleFor(command => command.TemplateId)
            .GreaterThan(0)
            .WithErrorCode("ReminderTemplate.InvalidId")
            .WithMessage("El identificador de la plantilla debe ser mayor que cero.");

        RuleFor(command => command.ScheduledAt)
            .NotNull()
            .WithErrorCode("Reminder.ScheduledAtRequired")
            .WithMessage("Debe indicar la fecha y hora del recordatorio.");
    }
}
