using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;

/// <summary>
/// Valida la forma de la serie con las mismas reglas del dominio, antes de consultar la base.
/// La fecha pasada y los traslapes se comprueban en el handler.
/// </summary>
public sealed class CreateAvailabilitySeriesCommandValidator : AbstractValidator<CreateAvailabilitySeriesCommand>
{
    // El comando no trae médico, que sale de la sesión; este valor válido solo completa Create.
    private const int AnyValidDoctorId = 1;

    private static readonly Dictionary<string, string> PropertyByErrorCode = new()
    {
        [DoctorAvailabilitySeriesErrors.DaysRequired.Code] = nameof(CreateAvailabilitySeriesCommand.DaysOfWeek),
        [DoctorAvailabilitySeriesErrors.InvalidDayOfWeek.Code] = nameof(CreateAvailabilitySeriesCommand.DaysOfWeek),
        [DoctorAvailabilitySeriesErrors.TimeNotOnHour.Code] = nameof(CreateAvailabilitySeriesCommand.StartTime),
        [DoctorAvailabilitySeriesErrors.InvalidTimeRange.Code] = nameof(CreateAvailabilitySeriesCommand.EndTime),
        [DoctorAvailabilitySeriesErrors.DurationTooShort.Code] = nameof(CreateAvailabilitySeriesCommand.EndTime),
        [DoctorAvailabilitySeriesErrors.InvalidDateRange.Code] = nameof(CreateAvailabilitySeriesCommand.EndDate)
    };

    public CreateAvailabilitySeriesCommandValidator(IOptions<SchedulingOptions> schedulingOptions)
    {
        var options = schedulingOptions.Value;

        RuleFor(command => command).Custom((command, context) =>
        {
            var days = CreateAvailabilitySeriesCommandHandler.ToDaysOfWeek(command.DaysOfWeek);
            var result = days.IsFailure
                ? days.Error
                : DoctorAvailabilitySeries.Create(
                    AnyValidDoctorId,
                    days.Value,
                    command.StartTime,
                    command.EndTime,
                    command.StartDate,
                    command.EndDate,
                    options.TimeZone,
                    options.MinimumBlockDurationHours).Error;

            if (result == Error.None) return;

            var property = PropertyByErrorCode.GetValueOrDefault(result.Code, nameof(CreateAvailabilitySeriesCommand));
            context.AddFailure(new ValidationFailure(property, result.Description) { ErrorCode = result.Code });
        });
    }
}
