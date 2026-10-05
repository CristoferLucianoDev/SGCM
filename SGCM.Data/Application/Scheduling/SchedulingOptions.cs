using Microsoft.Extensions.Options;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Application.Scheduling;

public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    public string TimeZone { get; set; } = string.Empty;
    public int AppointmentDurationHours { get; set; }
    public int MinimumBlockDurationHours { get; set; }
    public int MinimumBookingLeadDays { get; set; }
    public int BookingHorizonDays { get; set; }
    public int ConfirmationDeadlineHours { get; set; }
    public int PatientCancellationDeadlineHours { get; set; }
    public int PatientRescheduleDeadlineHours { get; set; }
    public int DoctorLateCancellationThresholdHours { get; set; }
    public int DoctorLateCancellationMinimumWords { get; set; }
    public int PatientReminderLeadHours { get; set; }
}

public sealed class SchedulingOptionsValidator : IValidateOptions<SchedulingOptions>
{
    public ValidateOptionsResult Validate(string? name, SchedulingOptions options)
    {
        var errors = new List<string>();

        if (!IsValidTimeZone(options.TimeZone))
        {
            errors.Add($"{OptionsRules.Describe("Scheduling:TimeZone")} debe ser una zona horaria válida, por ejemplo America/Santo_Domingo (valor actual: '{options.TimeZone}').");
        }

        OptionsRules.Positive(errors, "Scheduling:AppointmentDurationHours", options.AppointmentDurationHours);
        OptionsRules.Positive(errors, "Scheduling:MinimumBlockDurationHours", options.MinimumBlockDurationHours);
        OptionsRules.NonNegative(errors, "Scheduling:MinimumBookingLeadDays", options.MinimumBookingLeadDays);
        OptionsRules.Positive(errors, "Scheduling:BookingHorizonDays", options.BookingHorizonDays);
        OptionsRules.Positive(errors, "Scheduling:ConfirmationDeadlineHours", options.ConfirmationDeadlineHours);
        OptionsRules.Positive(errors, "Scheduling:PatientCancellationDeadlineHours", options.PatientCancellationDeadlineHours);
        OptionsRules.Positive(errors, "Scheduling:PatientRescheduleDeadlineHours", options.PatientRescheduleDeadlineHours);
        OptionsRules.Positive(errors, "Scheduling:DoctorLateCancellationThresholdHours", options.DoctorLateCancellationThresholdHours);
        OptionsRules.Positive(errors, "Scheduling:DoctorLateCancellationMinimumWords", options.DoctorLateCancellationMinimumWords);
        OptionsRules.Positive(errors, "Scheduling:PatientReminderLeadHours", options.PatientReminderLeadHours);

        if (options.MinimumBookingLeadDays >= options.BookingHorizonDays)
        {
            errors.Add("Scheduling:MinimumBookingLeadDays debe ser menor que Scheduling:BookingHorizonDays.");
        }

        if (options.AppointmentDurationHours > options.MinimumBlockDurationHours)
        {
            errors.Add("Scheduling:AppointmentDurationHours no puede ser mayor que Scheduling:MinimumBlockDurationHours, porque una cita debe caber en un solo bloque.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsValidTimeZone(string timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}