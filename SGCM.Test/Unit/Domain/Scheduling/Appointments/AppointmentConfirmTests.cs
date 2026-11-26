using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

public sealed class AppointmentConfirmTests
{
    private const int ThreeDaysHours = 72;

    private static readonly DateTimeOffset EarlyEnough = AppointmentMother.StartsAt.AddHours(-ThreeDaysHours);

    private static readonly DateTimeOffset DeadlineInstant =
        AppointmentMother.StartsAt.AddHours(-AppointmentMother.ConfirmationDeadlineHours);

    private static readonly DateTimeOffset OneSecondAfterDeadline = DeadlineInstant.AddSeconds(1);

    [Fact]
    public void Confirmar_una_cita_pendiente_dentro_del_plazo_la_deja_confirmada()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Confirm(appointment, EarlyEnough);

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Confirmed);
    }

    [Fact]
    public void Confirmar_emite_un_unico_AppointmentConfirmedEvent_con_la_cita()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Confirm(appointment, EarlyEnough);

        var domainEvent = appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentConfirmedEvent>();
        domainEvent.Appointment.ShouldBeSameAs(appointment);
    }

    [Fact]
    public void Confirmar_justo_en_el_limite_del_plazo_se_permite()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Confirm(appointment, DeadlineInstant);

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Confirmed);
    }

    [Fact]
    public void Confirmar_un_segundo_despues_del_plazo_se_rechaza_y_la_cita_no_cambia()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Confirm(appointment, OneSecondAfterDeadline);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AppointmentErrors.ConfirmationDeadlinePassed);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        appointment.Status.ShouldBe(AppointmentStatus.Pending);
        appointment.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public void Una_cita_que_no_esta_pendiente_no_se_puede_confirmar(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = Confirm(appointment, EarlyEnough);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AppointmentErrors.NotPending);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        appointment.Status.ShouldBe(status);
        appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void El_estado_se_valida_antes_que_el_plazo()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = Confirm(appointment, OneSecondAfterDeadline);

        result.Error.ShouldBe(AppointmentErrors.NotPending);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_plazo_que_no_es_positivo_es_un_error_de_programacion(int deadlineHours)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Should.Throw<ArgumentOutOfRangeException>(() => appointment.Confirm(EarlyEnough, deadlineHours));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Confirmar_esta_disponible_para_el_medico_exactamente_cuando_Confirm_tiene_exito(
        int secondsAfterDeadline, bool expected)
    {
        var now = DeadlineInstant.AddSeconds(secondsAfterDeadline);
        var available = AppointmentMother.InStatus(AppointmentStatus.Pending)
            .GetAllowedActions(AppointmentParticipant.Doctor, now, AppointmentMother.Rules)
            .Actions.Contains(AppointmentAction.Confirm);
        var succeeds = Confirm(AppointmentMother.InStatus(AppointmentStatus.Pending), now).IsSuccess;

        available.ShouldBe(expected);
        succeeds.ShouldBe(available);
    }

    private static Result Confirm(Appointment appointment, DateTimeOffset now) =>
        appointment.Confirm(now, AppointmentMother.ConfirmationDeadlineHours);
}
