using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.RescheduleAvailabilityBlock;

/// <summary>
/// Valida la forma de <see cref="RescheduleAvailabilityBlockCommand"/>: el bloque positivo, el horario nuevo con las
/// reglas de cualquier bloque (en punto, fin después del inicio y duración mínima) y el motivo dentro de su longitud.
/// La regla de semanas, el traslape y si el motivo es obligatorio se deciden en el handler.
/// </summary>
public sealed class RescheduleAvailabilityBlockCommandValidator : AbstractValidator<RescheduleAvailabilityBlockCommand>
{
    // El comando no trae médico, que sale de la sesión; este valor válido solo completa Create.
    private const int AnyValidDoctorId = 1;

    private static readonly Dictionary<string, string> PropertyByErrorCode = new()
    {
        [DoctorAvailabilityErrors.NotOnHour.Code] = nameof(RescheduleAvailabilityBlockCommand.StartTime),
        [DoctorAvailabilityErrors.InvalidTimeRange.Code] = nameof(RescheduleAvailabilityBlockCommand.EndTime),
        [DoctorAvailabilityErrors.DurationTooShort.Code] = nameof(RescheduleAvailabilityBlockCommand.EndTime)
    };

    public RescheduleAvailabilityBlockCommandValidator(BusinessCalendar calendar, IOptions<SchedulingOptions> schedulingOptions)
    {
        var minimumDurationHours = schedulingOptions.Value.MinimumBlockDurationHours;

        RuleFor(command => command.BlockId)
            .GreaterThan(0)
            .WithErrorCode("Availability.InvalidBlockId")
            .WithMessage("El identificador del bloque debe ser mayor que cero.");

        RuleFor(command => command).Custom((command, context) =>
        {
            var error = command.TryGetUtcRange(calendar, out var startsAt, out var endsAt)
                ? DoctorAvailability.Create(AnyValidDoctorId, seriesId: null, startsAt, endsAt, minimumDurationHours).Error
                : DoctorAvailabilityErrors.InvalidTimeRange;

            if (error == Error.None) return;

            var property = PropertyByErrorCode.GetValueOrDefault(error.Code, nameof(RescheduleAvailabilityBlockCommand));
            context.AddFailure(new ValidationFailure(property, error.Description) { ErrorCode = error.Code });
        });

        RuleFor(command => command.Reason)
            .Must(reason => reason is null || reason.Trim().Length <= StatusReason.MaxLength)
            .WithErrorCode(AppointmentErrors.StatusReasonTooLong.Code)
            .WithMessage(AppointmentErrors.StatusReasonTooLong.Description);
    }
}
