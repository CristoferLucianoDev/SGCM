using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;

/// <summary>
/// Valida la forma del bloque con las mismas reglas del dominio, antes de consultar la base.
/// La fecha pasada y los traslapes se comprueban en el handler.
/// </summary>
public sealed class CreateAvailabilityBlockCommandValidator : AbstractValidator<CreateAvailabilityBlockCommand>
{
    // El comando no trae médico, que sale de la sesión; este valor válido solo completa Create.
    private const int AnyValidDoctorId = 1;

    private static readonly Dictionary<string, string> PropertyByErrorCode = new()
    {
        [DoctorAvailabilityErrors.NotOnHour.Code] = nameof(CreateAvailabilityBlockCommand.StartTime),
        [DoctorAvailabilityErrors.InvalidTimeRange.Code] = nameof(CreateAvailabilityBlockCommand.EndTime),
        [DoctorAvailabilityErrors.DurationTooShort.Code] = nameof(CreateAvailabilityBlockCommand.EndTime)
    };

    public CreateAvailabilityBlockCommandValidator(BusinessCalendar calendar, IOptions<SchedulingOptions> schedulingOptions)
    {
        var minimumDurationHours = schedulingOptions.Value.MinimumBlockDurationHours;

        RuleFor(command => command).Custom((command, context) =>
        {
            var error = command.TryGetUtcRange(calendar, out var startsAt, out var endsAt)
                ? DoctorAvailability.Create(AnyValidDoctorId, seriesId: null, startsAt, endsAt, minimumDurationHours).Error
                : DoctorAvailabilityErrors.InvalidTimeRange;

            if (error == Error.None) return;

            var property = PropertyByErrorCode.GetValueOrDefault(error.Code, nameof(CreateAvailabilityBlockCommand));
            context.AddFailure(new ValidationFailure(property, error.Description) { ErrorCode = error.Code });
        });
    }
}
