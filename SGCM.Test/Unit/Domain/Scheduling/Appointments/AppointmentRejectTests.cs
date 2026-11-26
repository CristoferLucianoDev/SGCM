using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

public sealed class AppointmentRejectTests
{
    private const int ThreeDaysHours = 72;
    private const string Reason = "Agenda completa";
    private const string OneWordReason = "Ocupado";

    private static readonly DateTimeOffset EarlyEnough = AppointmentMother.StartsAt.AddHours(-ThreeDaysHours);

    private static readonly DateTimeOffset DeadlineInstant =
        AppointmentMother.StartsAt.AddHours(-AppointmentMother.ConfirmationDeadlineHours);

    private static readonly DateTimeOffset OneSecondAfterDeadline = DeadlineInstant.AddSeconds(1);

    [Fact]
    public void Rechazar_una_cita_pendiente_dentro_del_plazo_la_deja_rechazada_con_el_motivo_recortado()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reject(appointment, $"  {Reason}  ", EarlyEnough);

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Rejected);
        appointment.StatusReason.ShouldBe(Reason);
    }

    [Fact]
    public void Rechazar_emite_un_unico_AppointmentRejectedEvent_con_la_cita()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Reject(appointment, Reason, EarlyEnough);

        var domainEvent = appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRejectedEvent>();
        domainEvent.Appointment.ShouldBeSameAs(appointment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rechazar_sin_motivo_se_rechaza_y_la_cita_no_cambia(string? reason)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reject(appointment, reason, EarlyEnough);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AppointmentErrors.StatusReasonRequired);
        appointment.Status.ShouldBe(AppointmentStatus.Pending);
        appointment.StatusReason.ShouldBeNull();
        appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Un_motivo_de_la_longitud_maxima_se_acepta()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reject(appointment, new string('a', Appointment.StatusReasonMaxLength), EarlyEnough);

        result.IsSuccess.ShouldBeTrue();
        appointment.StatusReason!.Length.ShouldBe(Appointment.StatusReasonMaxLength);
    }

    [Fact]
    public void Un_motivo_demasiado_largo_se_rechaza_y_la_cita_no_cambia()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reject(appointment, new string('a', Appointment.StatusReasonMaxLength + 1), EarlyEnough);

        result.Error.ShouldBe(AppointmentErrors.StatusReasonTooLong);
        appointment.Status.ShouldBe(AppointmentStatus.Pending);
        appointment.StatusReason.ShouldBeNull();
        appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Un_motivo_de_una_sola_palabra_se_acepta_porque_no_hay_minimo_de_palabras()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reject(appointment, OneWordReason, EarlyEnough);

        result.IsSuccess.ShouldBeTrue();
        appointment.StatusReason.ShouldBe(OneWordReason);
    }

    [Fact]
    public void Rechazar_justo_en_el_limite_del_plazo_se_permite()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reject(appointment, Reason, DeadlineInstant);

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Rejected);
    }

    [Fact]
    public void Rechazar_fuera_del_plazo_se_rechaza_y_la_cita_no_cambia()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reject(appointment, Reason, OneSecondAfterDeadline);

        result.Error.ShouldBe(AppointmentErrors.ConfirmationDeadlinePassed);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        appointment.Status.ShouldBe(AppointmentStatus.Pending);
        appointment.StatusReason.ShouldBeNull();
        appointment.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public void Una_cita_que_no_esta_pendiente_no_se_puede_rechazar(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);
        var reasonBefore = appointment.StatusReason;

        var result = Reject(appointment, Reason, EarlyEnough);

        result.Error.ShouldBe(AppointmentErrors.NotPending);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        appointment.Status.ShouldBe(status);
        appointment.StatusReason.ShouldBe(reasonBefore);
        appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Una_cita_que_no_esta_pendiente_y_sin_motivo_se_rechaza_por_el_estado_primero()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = Reject(appointment, null, OneSecondAfterDeadline);

        result.Error.ShouldBe(AppointmentErrors.NotPending);
    }

    [Fact]
    public void Una_cita_pendiente_fuera_de_plazo_y_sin_motivo_se_rechaza_por_el_plazo_antes_que_por_el_motivo()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reject(appointment, null, OneSecondAfterDeadline);

        result.Error.ShouldBe(AppointmentErrors.ConfirmationDeadlinePassed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_plazo_que_no_es_positivo_es_un_error_de_programacion(int deadlineHours)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Should.Throw<ArgumentOutOfRangeException>(() => appointment.Reject(Reason, EarlyEnough, deadlineHours));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Rechazar_esta_disponible_para_el_medico_exactamente_cuando_Reject_tiene_exito(
        int secondsAfterDeadline, bool expected)
    {
        var now = DeadlineInstant.AddSeconds(secondsAfterDeadline);
        var available = AppointmentMother.InStatus(AppointmentStatus.Pending)
            .GetAllowedActions(AppointmentParticipant.Doctor, now, AppointmentMother.Rules)
            .Actions.Contains(AppointmentAction.Reject);
        var succeeds = Reject(AppointmentMother.InStatus(AppointmentStatus.Pending), Reason, now).IsSuccess;

        available.ShouldBe(expected);
        succeeds.ShouldBe(available);
    }

    private static Result Reject(Appointment appointment, string? reason, DateTimeOffset now) =>
        appointment.Reject(reason, now, AppointmentMother.ConfirmationDeadlineHours);
}
