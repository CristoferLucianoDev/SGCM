using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

public sealed class AffectedAppointmentsTests
{
    private const int ThresholdHours = 6;
    private const int MinimumWords = 30;
    private static readonly ShortNoticeCancellation ShortNotice = new(ThresholdHours, MinimumWords);

    // El bloque cerrado es el martes 12 de enero de 2027 de 12:00 a 18:00 UTC.
    private static readonly DateTimeOffset BlockStart = new(2027, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeRange Block = Range(BlockStart, BlockStart.AddHours(6));
    private static readonly DateTimeOffset LongBefore = BlockStart.AddDays(-7);

    [Fact]
    public void Solo_las_citas_que_se_cruzan_con_un_rango_quitado_quedan_afectadas_ordenadas_por_inicio()
    {
        var late = Appointment(1, BlockStart.AddHours(4));
        var early = Appointment(2, BlockStart);
        var before = Appointment(3, BlockStart.AddHours(-2));
        var after = Appointment(4, BlockStart.AddHours(6));

        var affected = Evaluate([late, early, before, after], [Block], LongBefore);

        affected.Appointments.Select(appointment => appointment.AppointmentId).ShouldBe(new[] { 2, 1 });
        affected.IsReasonRequired.ShouldBeTrue();
        affected.IsShortNotice.ShouldBeFalse();
    }

    [Fact]
    public void Una_cita_que_solo_toca_parte_de_un_rango_tambien_queda_afectada()
    {
        var crossing = Appointment(1, BlockStart.AddHours(-1));

        Evaluate([crossing], [Block], LongBefore).Appointments.ShouldHaveSingleItem().ShouldBe(crossing);
    }

    [Fact]
    public void Con_varios_rangos_quitados_cuenta_las_citas_de_todos()
    {
        var morning = Appointment(1, BlockStart);
        var middle = Appointment(2, BlockStart.AddHours(2));
        var evening = Appointment(3, BlockStart.AddHours(4));

        var affected = Evaluate([morning, middle, evening],
            [Range(BlockStart, BlockStart.AddHours(2)), Range(BlockStart.AddHours(4), BlockStart.AddHours(6))], LongBefore);

        affected.Appointments.Select(appointment => appointment.AppointmentId).ShouldBe(new[] { 1, 3 });
    }

    [Fact]
    public void Sin_citas_afectadas_el_motivo_es_opcional()
    {
        var affected = Evaluate([], [Block], LongBefore);

        affected.IsReasonRequired.ShouldBeFalse();
        affected.ValidateReason(null).ShouldBe(Error.None);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Con_citas_afectadas_el_motivo_es_obligatorio(string? reason)
    {
        Evaluate([Appointment(1, BlockStart)], [Block], LongBefore)
            .ValidateReason(reason).ShouldBe(AppointmentErrors.StatusReasonRequired);
    }

    [Fact]
    public void Con_tiempo_de_sobra_basta_un_motivo_corto()
    {
        Evaluate([Appointment(1, BlockStart)], [Block], LongBefore).ValidateReason("Congreso").ShouldBe(Error.None);
    }

    [Fact]
    public void Una_cita_que_empieza_en_menos_de_6_horas_vuelve_extendido_el_motivo()
    {
        var affected = Evaluate([Appointment(1, BlockStart)], [Block], BlockStart.AddHours(-ThresholdHours).AddMinutes(1));

        affected.IsShortNotice.ShouldBeTrue();
        affected.ValidateReason(Words(MinimumWords - 1))
            .ShouldBe(AppointmentErrors.ShortNoticeReasonTooShort(MinimumWords));
        affected.ValidateReason(Words(MinimumWords)).ShouldBe(Error.None);
    }

    [Fact]
    public void Una_cita_que_empieza_justo_a_las_6_horas_no_es_extendida()
    {
        Evaluate([Appointment(1, BlockStart)], [Block], BlockStart.AddHours(-ThresholdHours)).IsShortNotice.ShouldBeFalse();
    }

    [Fact]
    public void Una_cita_que_ya_empezo_tambien_es_extendida()
    {
        Evaluate([Appointment(1, BlockStart)], [Block], BlockStart.AddMinutes(30)).IsShortNotice.ShouldBeTrue();
    }

    [Fact]
    public void Las_palabras_se_cuentan_sin_importar_los_espacios_repetidos_ni_los_saltos_de_linea()
    {
        var affected = Evaluate([Appointment(1, BlockStart)], [Block], BlockStart.AddHours(-1));
        var reason = string.Join("  \n ", Enumerable.Repeat("palabra", MinimumWords));

        affected.ValidateReason(reason).ShouldBe(Error.None);
    }

    [Fact]
    public void El_motivo_respeta_la_longitud_maxima_de_la_cancelacion()
    {
        var affected = Evaluate([Appointment(1, BlockStart)], [Block], LongBefore);

        affected.ValidateReason(new string('A', StatusReason.MaxLength)).ShouldBe(Error.None);
        affected.ValidateReason(new string('A', StatusReason.MaxLength + 1)).ShouldBe(AppointmentErrors.StatusReasonTooLong);
    }

    [Fact]
    public void Un_motivo_que_pasa_la_politica_lo_acepta_la_cancelacion_de_cada_cita()
    {
        var appointment = AppointmentMother.Requested();
        var affected = Evaluate(
            [new BookedInterval(1, TimeRange.Create(appointment.StartsAt, appointment.EndsAt).Value)],
            [TimeRange.Create(appointment.StartsAt, appointment.EndsAt).Value],
            appointment.StartsAt.AddHours(-1));
        var reason = Words(MinimumWords);

        affected.ValidateReason(reason).ShouldBe(Error.None);
        appointment.Cancel(AppointmentCanceller.Doctor, reason, appointment.StartsAt.AddHours(-1), 24, ShortNotice)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void La_regla_de_cancelacion_tardia_es_obligatoria()
    {
        Should.Throw<ArgumentNullException>(() => AffectedAppointments.Evaluate([], [Block], LongBefore, null!));
    }

    private static AffectedAppointments Evaluate(BookedInterval[] appointments, TimeRange[] removed, DateTimeOffset now) =>
        AffectedAppointments.Evaluate(appointments, removed, now, ShortNotice);

    private static BookedInterval Appointment(int id, DateTimeOffset startsAt) => new(id, Range(startsAt, startsAt.AddHours(2)));

    private static TimeRange Range(DateTimeOffset start, DateTimeOffset end) => TimeRange.Create(start, end).Value;

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("palabra", count));
}
