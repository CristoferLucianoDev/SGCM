using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

/// <summary>
/// Pruebas de las acciones permitidas de una cita según quién la consulta, su estado y el instante. La cita de las
/// pruebas empieza en <see cref="AppointmentMother.StartsAt"/> y dura <see cref="AppointmentMother.DurationHours"/>.
/// </summary>
public sealed class AppointmentAllowedActionsTests
{
    private const int ThreeDaysHours = 72;
    private const int RescheduleDeadlineHours = AppointmentMother.PatientRescheduleDeadlineHours;
    private const int CancellationDeadlineHours = AppointmentMother.PatientCancellationDeadlineHours;
    private const int OneHourBeforeRescheduleDeadline = RescheduleDeadlineHours - 1;
    private const int OneHourBeforeCancellationDeadline = CancellationDeadlineHours - 1;
    private const int InsideShortNoticeHours = AppointmentMother.ShortNoticeThresholdHours - 1;
    private const int ShortNoticeThresholdHours = AppointmentMother.ShortNoticeThresholdHours;
    private const int ConfirmationDeadlineHours = AppointmentMother.ConfirmationDeadlineHours;
    private const int HoursIntoTheAppointment = 1;
    private const int ExpectedHoursLeftToConfirm = ThreeDaysHours - ConfirmationDeadlineHours;

    private static readonly DateTimeOffset StartsAt = AppointmentMother.StartsAt;

    private static readonly AppointmentAction[] PatientFullSet =
        [AppointmentAction.Cancel, AppointmentAction.Reschedule, AppointmentAction.ChangeDoctor];

    private static readonly AppointmentAction[] PatientOnlyOwnActions =
        [AppointmentAction.Confirm, AppointmentAction.Reject, AppointmentAction.Extend, AppointmentAction.Complete];

    [Theory]
    [InlineData(ThreeDaysHours)]
    [InlineData(RescheduleDeadlineHours)]
    public void El_paciente_con_tiempo_suficiente_puede_cancelar_reprogramar_y_cambiar_de_medico(int hoursBefore)
    {
        var allowed = Allowed(AppointmentStatus.Pending, AppointmentParticipant.Patient, HoursBefore(hoursBefore));

        allowed.Actions.ShouldBe(PatientFullSet);
    }

    [Fact]
    public void El_paciente_dentro_del_plazo_de_reprogramar_pero_con_plazo_de_cancelar_solo_puede_cancelar()
    {
        var allowed = Allowed(
            AppointmentStatus.Confirmed, AppointmentParticipant.Patient, HoursBefore(OneHourBeforeRescheduleDeadline));

        allowed.Actions.ShouldBe(new[] { AppointmentAction.Cancel });
    }

    [Fact]
    public void El_paciente_justo_en_el_limite_del_plazo_de_cancelar_todavia_puede_cancelar()
    {
        var allowed = Allowed(AppointmentStatus.Pending, AppointmentParticipant.Patient, HoursBefore(CancellationDeadlineHours));

        allowed.Actions.ShouldBe(new[] { AppointmentAction.Cancel });
    }

    [Fact]
    public void El_paciente_fuera_del_plazo_de_cancelar_no_tiene_acciones()
    {
        var allowed = Allowed(
            AppointmentStatus.Pending, AppointmentParticipant.Patient, HoursBefore(OneHourBeforeCancellationDeadline));

        allowed.Actions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public void El_paciente_no_tiene_acciones_en_una_cita_en_estado_final(AppointmentStatus status)
    {
        Allowed(status, AppointmentParticipant.Patient, HoursBefore(ThreeDaysHours)).Actions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending, ThreeDaysHours)]
    [InlineData(AppointmentStatus.Confirmed, ThreeDaysHours)]
    [InlineData(AppointmentStatus.Pending, OneHourBeforeRescheduleDeadline)]
    public void El_paciente_nunca_tiene_las_acciones_propias_del_medico_ni_exige_motivo_extendido(
        AppointmentStatus status, int hoursBefore)
    {
        var allowed = Allowed(status, AppointmentParticipant.Patient, HoursBefore(hoursBefore));

        allowed.Actions.ShouldNotContain(action => PatientOnlyOwnActions.Contains(action));
        allowed.RequiresExtendedCancellationReason.ShouldBeFalse();
    }

    [Fact]
    public void El_medico_con_una_cita_pendiente_con_tiempo_puede_cancelar_reprogramar_confirmar_y_rechazar()
    {
        var allowed = Allowed(AppointmentStatus.Pending, AppointmentParticipant.Doctor, HoursBefore(ThreeDaysHours));

        allowed.Actions.ShouldBe(new[]
        {
            AppointmentAction.Cancel, AppointmentAction.Reschedule, AppointmentAction.Confirm, AppointmentAction.Reject
        });
    }

    [Fact]
    public void El_medico_con_una_cita_pendiente_fuera_del_plazo_de_confirmar_no_puede_confirmar_ni_rechazar()
    {
        var allowed = Allowed(
            AppointmentStatus.Pending, AppointmentParticipant.Doctor, HoursBefore(OneHourBeforeRescheduleDeadline));

        allowed.Actions.ShouldBe(new[] { AppointmentAction.Cancel, AppointmentAction.Reschedule });
    }

    [Fact]
    public void El_medico_con_una_cita_confirmada_con_tiempo_puede_cancelar_y_reprogramar()
    {
        var allowed = Allowed(AppointmentStatus.Confirmed, AppointmentParticipant.Doctor, HoursBefore(ThreeDaysHours));

        allowed.Actions.ShouldBe(new[] { AppointmentAction.Cancel, AppointmentAction.Reschedule });
    }

    [Fact]
    public void El_medico_con_una_cita_confirmada_en_curso_puede_cancelar_extender_y_completar()
    {
        var allowed = Allowed(
            AppointmentStatus.Confirmed, AppointmentParticipant.Doctor, StartsAt.AddHours(HoursIntoTheAppointment));

        allowed.Actions.ShouldBe(new[] { AppointmentAction.Cancel, AppointmentAction.Extend, AppointmentAction.Complete });
    }

    [Fact]
    public void El_medico_con_una_cita_confirmada_ya_terminada_solo_puede_cancelar()
    {
        var endsAt = StartsAt.AddHours(AppointmentMother.DurationHours);

        var allowed = Allowed(AppointmentStatus.Confirmed, AppointmentParticipant.Doctor, endsAt);

        allowed.Actions.ShouldBe(new[] { AppointmentAction.Cancel });
    }

    [Theory]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public void El_medico_no_tiene_acciones_en_una_cita_en_estado_final(AppointmentStatus status)
    {
        Allowed(status, AppointmentParticipant.Doctor, HoursBefore(ThreeDaysHours)).Actions.ShouldBeEmpty();
    }

    [Fact]
    public void El_medico_que_cancela_con_poco_aviso_debe_dar_un_motivo_extendido()
    {
        var allowed = Allowed(
            AppointmentStatus.Confirmed, AppointmentParticipant.Doctor, HoursBefore(InsideShortNoticeHours));

        allowed.RequiresExtendedCancellationReason.ShouldBeTrue();
    }

    [Fact]
    public void El_medico_justo_en_el_umbral_de_poco_aviso_no_debe_dar_un_motivo_extendido()
    {
        var allowed = Allowed(
            AppointmentStatus.Confirmed, AppointmentParticipant.Doctor, HoursBefore(ShortNoticeThresholdHours));

        allowed.RequiresExtendedCancellationReason.ShouldBeFalse();
    }

    [Fact]
    public void El_paciente_con_poco_aviso_no_debe_dar_un_motivo_extendido_porque_no_puede_cancelar()
    {
        var allowed = Allowed(
            AppointmentStatus.Confirmed, AppointmentParticipant.Patient, HoursBefore(InsideShortNoticeHours));

        allowed.Actions.ShouldNotContain(AppointmentAction.Cancel);
        allowed.RequiresExtendedCancellationReason.ShouldBeFalse();
    }

    [Theory]
    [InlineData(AppointmentParticipant.Patient)]
    [InlineData(AppointmentParticipant.Doctor)]
    public void Una_cita_pendiente_informa_el_plazo_de_confirmacion_y_el_tiempo_que_falta(AppointmentParticipant viewer)
    {
        var allowed = Allowed(AppointmentStatus.Pending, viewer, HoursBefore(ThreeDaysHours));

        allowed.ConfirmationDeadline.ShouldBe(HoursBefore(ConfirmationDeadlineHours));
        allowed.TimeUntilConfirmationDeadline.ShouldBe(TimeSpan.FromHours(ExpectedHoursLeftToConfirm));
    }

    [Fact]
    public void Una_cita_pendiente_con_el_plazo_de_confirmacion_vencido_informa_cero_de_tiempo_restante()
    {
        var allowed = Allowed(
            AppointmentStatus.Pending, AppointmentParticipant.Doctor, HoursBefore(OneHourBeforeRescheduleDeadline));

        allowed.ConfirmationDeadline.ShouldBe(HoursBefore(ConfirmationDeadlineHours));
        allowed.TimeUntilConfirmationDeadline.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Una_cita_confirmada_no_informa_plazo_de_confirmacion()
    {
        var allowed = Allowed(AppointmentStatus.Confirmed, AppointmentParticipant.Doctor, HoursBefore(ThreeDaysHours));

        allowed.ConfirmationDeadline.ShouldBeNull();
        allowed.TimeUntilConfirmationDeadline.ShouldBeNull();
    }

    [Theory]
    [InlineData(CancellationDeadlineHours, 0, true)]
    [InlineData(CancellationDeadlineHours, 1, false)]
    public void Cancelar_esta_disponible_para_el_paciente_exactamente_cuando_Cancel_tendria_exito(
        int hoursBefore, int secondsLate, bool expected)
    {
        var now = HoursBefore(hoursBefore).AddSeconds(secondsLate);
        var available = Allowed(AppointmentStatus.Pending, AppointmentParticipant.Patient, now)
            .Actions.Contains(AppointmentAction.Cancel);
        var succeeds = AppointmentMother.InStatus(AppointmentStatus.Pending).Cancel(
            AppointmentCanceller.Patient, null, now,
            AppointmentMother.PatientCancellationDeadlineHours, AppointmentMother.ShortNotice).IsSuccess;

        available.ShouldBe(expected);
        succeeds.ShouldBe(available);
    }

    [Fact]
    public void Sin_reglas_es_un_error_de_programacion()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Should.Throw<ArgumentNullException>(() =>
            appointment.GetAllowedActions(AppointmentParticipant.Patient, StartsAt, null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(99)]
    public void Un_participante_no_contemplado_es_un_error_de_programacion(int viewer)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Should.Throw<ArgumentOutOfRangeException>(() =>
            appointment.GetAllowedActions((AppointmentParticipant)viewer, StartsAt, AppointmentMother.Rules));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Las_reglas_rechazan_un_plazo_de_cancelacion_que_no_es_positivo(int hours)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AppointmentRules(
            hours, AppointmentMother.PatientRescheduleDeadlineHours, AppointmentMother.ConfirmationDeadlineHours,
            AppointmentMother.ShortNotice));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Las_reglas_rechazan_un_plazo_de_reprogramacion_que_no_es_positivo(int hours)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AppointmentRules(
            AppointmentMother.PatientCancellationDeadlineHours, hours, AppointmentMother.ConfirmationDeadlineHours,
            AppointmentMother.ShortNotice));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Las_reglas_rechazan_un_plazo_de_confirmacion_que_no_es_positivo(int hours)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AppointmentRules(
            AppointmentMother.PatientCancellationDeadlineHours, AppointmentMother.PatientRescheduleDeadlineHours, hours,
            AppointmentMother.ShortNotice));
    }

    [Fact]
    public void Las_reglas_rechazan_una_regla_de_poco_aviso_nula()
    {
        Should.Throw<ArgumentNullException>(() => new AppointmentRules(
            AppointmentMother.PatientCancellationDeadlineHours, AppointmentMother.PatientRescheduleDeadlineHours,
            AppointmentMother.ConfirmationDeadlineHours, null!));
    }

    [Fact]
    public void Las_reglas_validas_conservan_sus_valores()
    {
        var rules = AppointmentMother.Rules;

        rules.PatientCancellationDeadlineHours.ShouldBe(AppointmentMother.PatientCancellationDeadlineHours);
        rules.PatientRescheduleDeadlineHours.ShouldBe(AppointmentMother.PatientRescheduleDeadlineHours);
        rules.ConfirmationDeadlineHours.ShouldBe(AppointmentMother.ConfirmationDeadlineHours);
        rules.ShortNotice.ShouldBe(AppointmentMother.ShortNotice);
    }

    private static DateTimeOffset HoursBefore(int hours) => StartsAt.AddHours(-hours);

    private static AllowedAppointmentActions Allowed(
        AppointmentStatus status, AppointmentParticipant viewer, DateTimeOffset now) =>
        AppointmentMother.InStatus(status).GetAllowedActions(viewer, now, AppointmentMother.Rules);
}
