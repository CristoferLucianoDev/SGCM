using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

/// <summary>Pruebas de <see cref="Appointment.Complete"/>: estado, bordes del horario y coherencia con las acciones.</summary>
public sealed class AppointmentCompleteTests
{
    private const int HoursIntoTheAppointment = 1;

    private static readonly DateTimeOffset StartsAt = AppointmentMother.StartsAt;
    private static readonly DateTimeOffset EndsAt = StartsAt.AddHours(AppointmentMother.DurationHours);
    private static readonly DateTimeOffset Midway = StartsAt.AddHours(HoursIntoTheAppointment);

    [Fact]
    public void Completar_una_cita_en_curso_la_deja_completada_con_el_fin_real()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.Complete(Midway);

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Completed);
        appointment.EndsAt.ShouldBe(Midway);
    }

    [Fact]
    public void Completar_emite_un_unico_AppointmentCompletedEvent_con_la_cita()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        appointment.Complete(Midway);

        var domainEvent = appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentCompletedEvent>();
        domainEvent.Appointment.ShouldBeSameAs(appointment);
    }

    [Fact]
    public void Completar_justo_en_el_inicio_es_NotInProgress_y_la_cita_no_cambia()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.Complete(StartsAt);

        AssertNotInProgress(appointment, result, AppointmentStatus.Confirmed);
    }

    [Fact]
    public void Completar_un_instante_despues_del_inicio_se_permite()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        var now = StartsAt.AddSeconds(1);

        var result = appointment.Complete(now);

        result.IsSuccess.ShouldBeTrue();
        appointment.EndsAt.ShouldBe(now);
    }

    [Fact]
    public void Completar_antes_del_inicio_es_NotInProgress()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.Complete(StartsAt.AddSeconds(-1));

        AssertNotInProgress(appointment, result, AppointmentStatus.Confirmed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Completar_en_el_fin_exacto_o_despues_es_NotInProgress(int secondsAfterEnd)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.Complete(EndsAt.AddSeconds(secondsAfterEnd));

        AssertNotInProgress(appointment, result, AppointmentStatus.Confirmed);
    }

    [Fact]
    public void Completar_una_cita_pendiente_es_NotInProgress()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = appointment.Complete(Midway);

        AssertNotInProgress(appointment, result, AppointmentStatus.Pending);
    }

    [Theory]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public void Completar_una_cita_en_estado_final_es_NotInProgress(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = appointment.Complete(Midway);

        AssertNotInProgress(appointment, result, status);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Completar_esta_disponible_para_el_medico_exactamente_cuando_Complete_tiene_exito(
        int secondsAfterStart, bool expected)
    {
        var now = StartsAt.AddSeconds(secondsAfterStart);
        var available = AppointmentMother.InStatus(AppointmentStatus.Confirmed)
            .GetAllowedActions(AppointmentParticipant.Doctor, now, AppointmentMother.Rules)
            .Actions.Contains(AppointmentAction.Complete);
        var succeeds = AppointmentMother.InStatus(AppointmentStatus.Confirmed).Complete(now).IsSuccess;

        available.ShouldBe(expected);
        succeeds.ShouldBe(available);
    }

    private static void AssertNotInProgress(Appointment appointment, Result result, AppointmentStatus status)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AppointmentErrors.NotInProgress);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        appointment.Status.ShouldBe(status);
        appointment.EndsAt.ShouldBe(EndsAt);
        appointment.DomainEvents.ShouldBeEmpty();
    }
}
