using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Commands.ConfirmAppointment;

/// <summary>
/// Valida la forma de <see cref="ConfirmAppointmentCommand"/>: el identificador de la cita debe ser mayor que cero.
/// </summary>
public sealed class ConfirmAppointmentCommandValidator : AbstractValidator<ConfirmAppointmentCommand>
{
    public ConfirmAppointmentCommandValidator()
    {
        RuleFor(command => command.AppointmentId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidAppointmentId")
            .WithMessage("El identificador de la cita debe ser mayor que cero.");
    }
}
