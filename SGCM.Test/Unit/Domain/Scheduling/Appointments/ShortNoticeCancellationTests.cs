using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

public sealed class ShortNoticeCancellationTests
{
    private static readonly DateTimeOffset StartsAt = AppointmentMother.StartsAt;

    private readonly ShortNoticeCancellation _rule = AppointmentMother.ShortNotice;

    [Fact]
    public void A_exactamente_el_umbral_no_aplica()
    {
        var now = StartsAt.AddHours(-AppointmentMother.ShortNoticeThresholdHours);

        _rule.AppliesTo(StartsAt, now).ShouldBeFalse();
    }

    [Fact]
    public void A_un_segundo_menos_que_el_umbral_aplica()
    {
        var now = StartsAt.AddHours(-AppointmentMother.ShortNoticeThresholdHours).AddSeconds(1);

        _rule.AppliesTo(StartsAt, now).ShouldBeTrue();
    }

    [Fact]
    public void Despues_del_inicio_aplica()
    {
        _rule.AppliesTo(StartsAt, StartsAt.AddHours(1)).ShouldBeTrue();
    }

    [Fact]
    public void Con_mas_horas_que_el_umbral_no_aplica()
    {
        _rule.AppliesTo(StartsAt, StartsAt.AddHours(-(AppointmentMother.ShortNoticeThresholdHours + 1))).ShouldBeFalse();
    }

    [Fact]
    public void Conserva_los_valores_de_la_configuracion()
    {
        _rule.ThresholdHours.ShouldBe(AppointmentMother.ShortNoticeThresholdHours);
        _rule.MinimumWords.ShouldBe(AppointmentMother.ShortNoticeMinimumWords);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_umbral_que_no_es_positivo_es_un_error_de_programacion(int thresholdHours)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ShortNoticeCancellation(thresholdHours, AppointmentMother.ShortNoticeMinimumWords));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_minimo_de_palabras_que_no_es_positivo_es_un_error_de_programacion(int minimumWords)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ShortNoticeCancellation(AppointmentMother.ShortNoticeThresholdHours, minimumWords));
    }
}
