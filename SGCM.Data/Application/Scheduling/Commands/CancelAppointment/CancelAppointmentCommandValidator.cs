using FluentValidation;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.CancelAppointment;

/// <summary>
/// Valida la forma de <see cref="CancelAppointmentCommand"/>: el identificador de la cita debe ser mayor que
/// cero y el motivo respeta su longitud máxima.
/// </summary>
public sealed class CancelAppointmentCommandValidator : AbstractValidator<CancelAppointmentCommand>
{
    public CancelAppointmentCommandValidator()
    {
        RuleFor(command => command.AppointmentId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidAppointmentId")
            .WithMessage("El identificador de la cita debe ser mayor que cero.");

        RuleFor(command => command.Reason)
            .Must(reason => reason is null || reason.Trim().Length <= Appointment.StatusReasonMaxLength)
            .WithErrorCode(AppointmentErrors.StatusReasonTooLong.Code)
            .WithMessage(AppointmentErrors.StatusReasonTooLong.Description);
    }
}
