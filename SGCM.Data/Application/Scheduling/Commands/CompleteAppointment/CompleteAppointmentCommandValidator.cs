using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Commands.CompleteAppointment;

/// <summary>
/// Valida la forma de <see cref="CompleteAppointmentCommand"/>: el identificador de la cita debe ser mayor que cero.
/// </summary>
public sealed class CompleteAppointmentCommandValidator : AbstractValidator<CompleteAppointmentCommand>
{
    public CompleteAppointmentCommandValidator()
    {
        RuleFor(command => command.AppointmentId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidAppointmentId")
            .WithMessage("El identificador de la cita debe ser mayor que cero.");
    }
}
