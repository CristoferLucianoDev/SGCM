using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

public sealed class AppointmentExpireTests
{
    private const int ThreeDaysHours = 72;
    private const int HoursInsideLastWindow = 47;

    private static readonly DateTimeOffset DeadlineInstant =
        AppointmentMother.StartsAt.AddHours(-AppointmentMother.ConfirmationDeadlineHours);

    private static readonly DateTimeOffset OneSecondBeforeDeadline = DeadlineInstant.AddSeconds(-1);

    private static readonly DateTimeOffset OneSecondAfterDeadline = DeadlineInstant.AddSeconds(1);

    [Fact]
    public void Vencer_una_pendiente_pasado_el_plazo_la_deja_cancelada_con_el_motivo_de_vencimiento()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Expire(appointment, AppointmentMother.StartsAt.AddHours(-HoursInsideLastWindow));

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
        appointment.StatusReason.ShouldBe(Appointment.ExpiredReason);
    }

    [Fact]
    public void Vencer_emite_un_unico_AppointmentExpiredEvent_con_la_cita_y_no_el_de_cancelacion()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Expire(appointment, AppointmentMother.StartsAt.AddHours(-HoursInsideLastWindow));

        var domainEvent = appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentExpiredEvent>();
        domainEvent.Appointment.ShouldBeSameAs(appointment);
        appointment.DomainEvents.OfType<AppointmentCancelledEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void Vencer_justo_en_el_limite_del_plazo_se_rechaza_y_la_cita_no_cambia()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Expire(appointment, DeadlineInstant);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AppointmentErrors.ConfirmationDeadlineNotReached);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        appointment.Status.ShouldBe(AppointmentStatus.Pending);
        appointment.StatusReason.ShouldBeNull();
        appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Vencer_un_segundo_despues_del_limite_del_plazo_vence_la_cita()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Expire(appointment, OneSecondAfterDeadline);

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
    }

    [Fact]
    public void Vencer_despues_del_inicio_de_la_cita_tambien_la_vence()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Expire(appointment, appointment.StartsAt.AddHours(1));

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
        appointment.StatusReason.ShouldBe(Appointment.ExpiredReason);
    }

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public void Una_cita_que_no_esta_pendiente_no_vence(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);
        var reasonBefore = appointment.StatusReason;
        var eventsBefore = appointment.DomainEvents.Count;

        var result = Expire(appointment, OneSecondAfterDeadline);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AppointmentErrors.NotPending);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        appointment.Status.ShouldBe(status);
        appointment.StatusReason.ShouldBe(reasonBefore);
        appointment.DomainEvents.Count.ShouldBe(eventsBefore);
        appointment.DomainEvents.OfType<AppointmentExpiredEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void El_estado_se_valida_antes_que_el_plazo()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = Expire(appointment, AppointmentMother.StartsAt.AddHours(-ThreeDaysHours));

        result.Error.ShouldBe(AppointmentErrors.NotPending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void En_los_bordes_del_plazo_exactamente_una_de_Confirm_y_Expire_tiene_exito(bool afterDeadline)
    {
        var now = afterDeadline ? OneSecondAfterDeadline : DeadlineInstant;
        var toConfirm = AppointmentMother.InStatus(AppointmentStatus.Pending);
        var toExpire = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var confirmed = toConfirm.Confirm(now, AppointmentMother.ConfirmationDeadlineHours).IsSuccess;
        var expired = Expire(toExpire, now).IsSuccess;

        confirmed.ShouldNotBe(expired);
        expired.ShouldBe(afterDeadline);
    }

    [Fact]
    public void Un_segundo_antes_del_limite_solo_Confirm_tiene_exito()
    {
        var toConfirm = AppointmentMother.InStatus(AppointmentStatus.Pending);
        var toExpire = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var confirmed = toConfirm.Confirm(OneSecondBeforeDeadline, AppointmentMother.ConfirmationDeadlineHours).IsSuccess;
        var expired = Expire(toExpire, OneSecondBeforeDeadline).IsSuccess;

        confirmed.ShouldBeTrue();
        expired.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_plazo_que_no_es_positivo_es_un_error_de_programacion(int deadlineHours)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Should.Throw<ArgumentOutOfRangeException>(() => appointment.Expire(OneSecondAfterDeadline, deadlineHours));
    }

    private static Result Expire(Appointment appointment, DateTimeOffset now) =>
        appointment.Expire(now, AppointmentMother.ConfirmationDeadlineHours);
}
