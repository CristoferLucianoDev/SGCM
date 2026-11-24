using SGCM.Data.Application.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Application;

public sealed class SchedulingOptionsTests
{
    private const int ThresholdHours = 6;
    private const int MinimumWords = 30;

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
}
