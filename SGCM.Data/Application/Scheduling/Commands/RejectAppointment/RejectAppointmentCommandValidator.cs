using FluentValidation;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.RejectAppointment;

/// <summary>
/// Valida la forma de <see cref="RejectAppointmentCommand"/>: el identificador de la cita debe ser mayor que cero
/// y el motivo debe venir y respetar su longitud máxima.
/// </summary>
public sealed class RejectAppointmentCommandValidator : AbstractValidator<RejectAppointmentCommand>
{
    public RejectAppointmentCommandValidator()
    {
        RuleFor(command => command.AppointmentId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidAppointmentId")
            .WithMessage("El identificador de la cita debe ser mayor que cero.");

        RuleFor(command => command.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithErrorCode(AppointmentErrors.StatusReasonRequired.Code)
            .WithMessage(AppointmentErrors.StatusReasonRequired.Description);

        RuleFor(command => command.Reason)
            .Must(reason => reason!.Trim().Length <= Appointment.StatusReasonMaxLength)
            .When(command => !string.IsNullOrWhiteSpace(command.Reason))
            .WithErrorCode(AppointmentErrors.StatusReasonTooLong.Code)
            .WithMessage(AppointmentErrors.StatusReasonTooLong.Description);
    }
}
