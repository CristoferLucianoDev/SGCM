using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.UpdateAvailabilitySeries;

/// <summary>
/// Valida la forma de <see cref="UpdateAvailabilitySeriesCommand"/>: la serie positiva, el patrón semanal con las reglas
/// de cualquier serie y el motivo dentro de su longitud. La fecha desde la que aplica, que la fecha hasta no quede antes
/// de ella, el traslape y si el motivo es obligatorio se deciden en el handler.
/// </summary>
public sealed class UpdateAvailabilitySeriesCommandValidator : AbstractValidator<UpdateAvailabilitySeriesCommand>
{
    // El comando no trae médico ni fecha desde; estos valores válidos solo completan Create.
    private const int AnyValidDoctorId = 1;

    private static readonly Dictionary<string, string> PropertyByErrorCode = new()
    {
        [DoctorAvailabilitySeriesErrors.DaysRequired.Code] = nameof(UpdateAvailabilitySeriesCommand.DaysOfWeek),
        [DoctorAvailabilitySeriesErrors.InvalidDayOfWeek.Code] = nameof(UpdateAvailabilitySeriesCommand.DaysOfWeek),
        [DoctorAvailabilitySeriesErrors.TimeNotOnHour.Code] = nameof(UpdateAvailabilitySeriesCommand.StartTime),
        [DoctorAvailabilitySeriesErrors.InvalidTimeRange.Code] = nameof(UpdateAvailabilitySeriesCommand.EndTime),
        [DoctorAvailabilitySeriesErrors.DurationTooShort.Code] = nameof(UpdateAvailabilitySeriesCommand.EndTime)
    };

    public UpdateAvailabilitySeriesCommandValidator(IOptions<SchedulingOptions> schedulingOptions)
    {
        var options = schedulingOptions.Value;

        RuleFor(command => command.SeriesId)
            .GreaterThan(0)
            .WithErrorCode("AvailabilitySeries.InvalidSeriesId")
            .WithMessage("El identificador de la serie debe ser mayor que cero.");

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
                    DateOnly.MinValue,
                    endDate: null,
                    options.TimeZone,
                    options.MinimumBlockDurationHours).Error;

            if (result == Error.None) return;

            var property = PropertyByErrorCode.GetValueOrDefault(result.Code, nameof(UpdateAvailabilitySeriesCommand));
            context.AddFailure(new ValidationFailure(property, result.Description) { ErrorCode = result.Code });
        });

        RuleFor(command => command.Reason)
            .Must(reason => reason is null || reason.Trim().Length <= StatusReason.MaxLength)
            .WithErrorCode(AppointmentErrors.StatusReasonTooLong.Code)
            .WithMessage(AppointmentErrors.StatusReasonTooLong.Description);
    }
}
