using FluentValidation;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.CloseAvailabilityBlock;

/// <summary>
/// Valida la forma de <see cref="CloseAvailabilityBlockCommand"/>: el bloque debe ser positivo y el motivo, si se
/// envía, cabe en el motivo de cancelación de las citas. Si el motivo es obligatorio lo decide la política.
/// </summary>
public sealed class CloseAvailabilityBlockCommandValidator : AbstractValidator<CloseAvailabilityBlockCommand>
{
    public CloseAvailabilityBlockCommandValidator()
    {
        RuleFor(command => command.BlockId)
            .GreaterThan(0)
            .WithErrorCode("Availability.InvalidBlockId")
            .WithMessage("El identificador del bloque debe ser mayor que cero.");

        RuleFor(command => command.Reason)
            .Must(reason => reason is null || reason.Trim().Length <= StatusReason.MaxLength)
            .WithErrorCode(AppointmentErrors.StatusReasonTooLong.Code)
            .WithMessage(AppointmentErrors.StatusReasonTooLong.Description);
    }
}
