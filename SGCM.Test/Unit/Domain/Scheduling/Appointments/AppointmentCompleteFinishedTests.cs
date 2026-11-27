using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

/// <summary>Pruebas de <see cref="Appointment.CompleteFinished"/>: estado, borde del fin y complemento con Complete.</summary>
public sealed class AppointmentCompleteFinishedTests
{
    private static readonly DateTimeOffset EndsAt = AppointmentMother.StartsAt.AddHours(AppointmentMother.DurationHours);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3600)]
    public void Una_cita_confirmada_en_el_fin_exacto_o_despues_queda_completada_con_el_mismo_fin(int secondsAfterEnd)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.CompleteFinished(EndsAt.AddSeconds(secondsAfterEnd));

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Completed);
        appointment.EndsAt.ShouldBe(EndsAt);
    }

    [Fact]
    public void Completar_una_cita_terminada_emite_un_unico_AppointmentCompletedEvent_con_la_cita()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        appointment.CompleteFinished(EndsAt);

        var domainEvent = appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentCompletedEvent>();
        domainEvent.Appointment.ShouldBeSameAs(appointment);
    }

    [Fact]
    public void Un_segundo_antes_del_fin_es_NotFinished_y_la_cita_no_cambia()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.CompleteFinished(EndsAt.AddSeconds(-1));

        AssertRejected(appointment, result, AppointmentErrors.NotFinished, AppointmentStatus.Confirmed);
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public void Una_cita_que_no_esta_confirmada_es_NotConfirmed_y_no_cambia(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = appointment.CompleteFinished(EndsAt);

        AssertRejected(appointment, result, AppointmentErrors.NotConfirmed, status);
    }

    [Fact]
    public void El_estado_se_valida_antes_que_la_hora()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = appointment.CompleteFinished(EndsAt.AddSeconds(-1));

        result.Error.ShouldBe(AppointmentErrors.NotConfirmed);
    }

    [Theory]
    [InlineData(-1, true, false)]
    [InlineData(0, false, true)]
    public void En_los_bordes_del_fin_exactamente_uno_de_Complete_y_CompleteFinished_tiene_exito(
        int secondsFromEnd, bool completeSucceeds, bool completeFinishedSucceeds)
    {
        var now = EndsAt.AddSeconds(secondsFromEnd);
        var forComplete = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        var forCompleteFinished = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var complete = forComplete.Complete(now).IsSuccess;
        var completeFinished = forCompleteFinished.CompleteFinished(now).IsSuccess;

        complete.ShouldBe(completeSucceeds);
        completeFinished.ShouldBe(completeFinishedSucceeds);
        (complete ^ completeFinished).ShouldBeTrue();
    }

    private static void AssertRejected(Appointment appointment, Result result, Error error, AppointmentStatus status)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        appointment.Status.ShouldBe(status);
        appointment.EndsAt.ShouldBe(EndsAt);
        appointment.DomainEvents.ShouldBeEmpty();
    }
}
