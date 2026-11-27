using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Commands.RescheduleAppointmentByDoctor;

/// <summary>
/// Valida la forma de <see cref="RescheduleAppointmentByDoctorCommand"/>: el identificador de la cita debe ser mayor
/// que cero y la nueva hora es obligatoria.
/// </summary>
public sealed class RescheduleAppointmentByDoctorCommandValidator : AbstractValidator<RescheduleAppointmentByDoctorCommand>
{
    public RescheduleAppointmentByDoctorCommandValidator()
    {
        RuleFor(command => command.AppointmentId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidAppointmentId")
            .WithMessage("El identificador de la cita debe ser mayor que cero.");

        RuleFor(command => command.StartsAt)
            .NotNull()
            .WithErrorCode("Appointment.StartsAtRequired")
            .WithMessage("Debe indicar la nueva hora de la cita.");
    }
}
