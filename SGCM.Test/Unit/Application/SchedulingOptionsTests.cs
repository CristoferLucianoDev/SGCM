using SGCM.Data.Application.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Application;

public sealed class SchedulingOptionsTests
{
    private const int ThresholdHours = 6;
    private const int MinimumWords = 30;
    private const int CancellationHours = 24;
    private const int RescheduleHours = 48;
    private const int ConfirmationHours = 36;

    [Fact]
    public void ToShortNoticeCancellation_usa_el_umbral_y_las_palabras_configurados()
    {
        var options = new SchedulingOptions
        {
            DoctorLateCancellationThresholdHours = ThresholdHours,
            DoctorLateCancellationMinimumWords = MinimumWords
        };

        var rule = options.ToShortNoticeCancellation();

        rule.ThresholdHours.ShouldBe(ThresholdHours);
        rule.MinimumWords.ShouldBe(MinimumWords);
    }

    [Fact]
    public void ToAppointmentRules_usa_los_plazos_y_la_regla_de_poco_aviso_configurados()
    {
        var options = new SchedulingOptions
        {
            PatientCancellationDeadlineHours = CancellationHours,
            PatientRescheduleDeadlineHours = RescheduleHours,
            ConfirmationDeadlineHours = ConfirmationHours,
            DoctorLateCancellationThresholdHours = ThresholdHours,
            DoctorLateCancellationMinimumWords = MinimumWords
        };

        var rules = options.ToAppointmentRules();

        rules.PatientCancellationDeadlineHours.ShouldBe(CancellationHours);
        rules.PatientRescheduleDeadlineHours.ShouldBe(RescheduleHours);
        rules.ConfirmationDeadlineHours.ShouldBe(ConfirmationHours);
        rules.ShortNotice.ShouldBe(options.ToShortNoticeCancellation());
    }
}
