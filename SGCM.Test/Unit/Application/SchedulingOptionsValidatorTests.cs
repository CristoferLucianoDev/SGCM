using Shouldly;
using SGCM.Data.Application.Scheduling;

namespace SGCM.Test.Unit.Application;

public class SchedulingOptionsValidatorTests
{
    private readonly SchedulingOptionsValidator _validator = new();

    private static SchedulingOptions ValidOptions() => new()
    {
        TimeZone = "America/Santo_Domingo",
        AppointmentDurationHours = 2,
        MinimumBlockDurationHours = 6,
        MinimumBookingLeadDays = 4,
        BookingHorizonDays = 90,
        ConfirmationDeadlineHours = 48,
        PatientCancellationDeadlineHours = 24,
        PatientRescheduleDeadlineHours = 48,
        DoctorLateCancellationThresholdHours = 6,
        DoctorLateCancellationMinimumWords = 30,
        PatientReminderLeadHours = 24
    };

    [Fact]
    public void Valid_configuration_passes()
    {
        _validator.Validate(null, ValidOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Unknown_time_zone_fails_and_names_the_key()
    {
        var options = ValidOptions();
        options.TimeZone = "Zona/Inexistente";

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Scheduling:TimeZone");
    }

    [Fact]
    public void Lead_days_equal_to_horizon_fails()
    {
        var options = ValidOptions();
        options.MinimumBookingLeadDays = 90;

        _validator.Validate(null, options).Failed.ShouldBeTrue();
    }
}