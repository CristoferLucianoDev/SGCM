using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

/// <summary>
/// Pruebas de <see cref="Appointment.RescheduleByPatient"/>: estado, plazo, nueva hora, mismo inicio y coherencia con
/// las acciones permitidas del paciente.
/// </summary>
public sealed class AppointmentRescheduleByPatientTests
{
    private const int ThreeDaysHours = 72;
    private const int OtherOffsetHours = 2;
    private const int HalfHourMinutes = 30;

    private static readonly DateTimeOffset EarlyEnough = AppointmentMother.StartsAt.AddHours(-ThreeDaysHours);

    private static readonly DateTimeOffset DeadlineInstant =
        AppointmentMother.StartsAt.AddHours(-AppointmentMother.PatientRescheduleDeadlineHours);

    private static readonly DateTimeOffset OneSecondAfterDeadline = DeadlineInstant.AddSeconds(1);

    // Miércoles 20 de enero de 2027 a las 10:00 en Santo Domingo: en punto y dentro de la ventana de reservas.
    private static readonly DateTimeOffset NewStartsAt = new(2027, 1, 20, 10, 0, 0, AppointmentMother.SantoDomingo);

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    public void Reprogramar_una_cita_activa_la_deja_pendiente_con_el_nuevo_inicio_y_el_fin_segun_la_duracion(
        AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = Reschedule(appointment, NewStartsAt, EarlyEnough);

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Pending);
        appointment.StartsAt.ShouldBe(NewStartsAt);
        appointment.EndsAt.ShouldBe(NewStartsAt.AddHours(AppointmentMother.DurationHours));
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    public void Reprogramar_emite_un_unico_AppointmentRescheduledEvent_con_el_paciente_y_el_inicio_anterior(
        AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);
        var previousStartsAt = appointment.StartsAt;

        Reschedule(appointment, NewStartsAt, EarlyEnough);

        var domainEvent = appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRescheduledEvent>();
        domainEvent.Appointment.ShouldBeSameAs(appointment);
        domainEvent.RescheduledBy.ShouldBe(AppointmentParticipant.Patient);
        domainEvent.PreviousStartsAt.ShouldBe(previousStartsAt);
    }

    [Fact]
    public void Un_inicio_con_otro_desfase_se_guarda_en_UTC()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
        var otherOffset = new DateTimeOffset(2027, 1, 20, 10, 0, 0, TimeSpan.FromHours(OtherOffsetHours));

        Reschedule(appointment, otherOffset, EarlyEnough);

        appointment.StartsAt.ShouldBe(otherOffset);
        appointment.StartsAt.Offset.ShouldBe(TimeSpan.Zero);
        appointment.EndsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Reprogramar_justo_en_el_limite_del_plazo_se_permite()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reschedule(appointment, NewStartsAt, DeadlineInstant);

        result.IsSuccess.ShouldBeTrue();
        appointment.StartsAt.ShouldBe(NewStartsAt);
    }

    [Fact]
    public void Reprogramar_un_segundo_despues_del_plazo_se_rechaza_y_pide_llamar_al_medico()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reschedule(appointment, NewStartsAt, OneSecondAfterDeadline);

        AssertRejected(appointment, result, AppointmentErrors.RescheduleDeadlinePassed, AppointmentStatus.Pending);
        result.Error.Description.ShouldContain("llamar al médico");
    }

    [Fact]
    public void Una_hora_antes_de_la_antelacion_minima_es_TooSoon()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reschedule(appointment, AppointmentMother.WindowStart.AddHours(-1), EarlyEnough);

        AssertRejected(appointment, result, AppointmentErrors.TooSoon, AppointmentStatus.Pending);
    }

    [Fact]
    public void Una_hora_desde_el_fin_del_horizonte_es_BeyondHorizon()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reschedule(appointment, AppointmentMother.WindowEnd, EarlyEnough);

        AssertRejected(appointment, result, AppointmentErrors.BeyondHorizon, AppointmentStatus.Pending);
    }

    [Fact]
    public void Una_hora_que_no_esta_en_punto_es_NotOnHour()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reschedule(appointment, NewStartsAt.AddMinutes(HalfHourMinutes), EarlyEnough);

        AssertRejected(appointment, result, AppointmentErrors.NotOnHour, AppointmentStatus.Pending);
    }

    [Fact]
    public void El_primer_instante_de_la_ventana_se_acepta()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reschedule(appointment, AppointmentMother.WindowStart, EarlyEnough);

        result.IsSuccess.ShouldBeTrue();
        appointment.StartsAt.ShouldBe(AppointmentMother.WindowStart);
    }

    [Fact]
    public void El_mismo_inicio_es_SameSchedule()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = Reschedule(appointment, appointment.StartsAt, EarlyEnough);

        AssertRejected(appointment, result, AppointmentErrors.SameSchedule, AppointmentStatus.Confirmed);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void El_mismo_inicio_con_otro_desfase_tambien_es_SameSchedule()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reschedule(appointment, appointment.StartsAt.ToOffset(TimeSpan.Zero), EarlyEnough);

        result.Error.ShouldBe(AppointmentErrors.SameSchedule);
    }

    [Theory]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public void Una_cita_en_estado_final_es_NotActive(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = Reschedule(appointment, NewStartsAt, EarlyEnough);

        AssertRejected(appointment, result, AppointmentErrors.NotActive, status);
    }

    [Fact]
    public void El_estado_se_valida_antes_que_el_plazo()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Cancelled);

        var result = Reschedule(appointment, NewStartsAt, OneSecondAfterDeadline);

        result.Error.ShouldBe(AppointmentErrors.NotActive);
    }

    [Fact]
    public void El_plazo_se_valida_antes_que_la_nueva_hora()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reschedule(appointment, NewStartsAt.AddMinutes(HalfHourMinutes), OneSecondAfterDeadline);

        result.Error.ShouldBe(AppointmentErrors.RescheduleDeadlinePassed);
    }

    [Fact]
    public void La_nueva_hora_se_valida_antes_que_el_mismo_inicio()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
        // Ventana que empieza después de la cita: su propio inicio queda fuera y se informa TooSoon, no SameSchedule.
        var laterWindow = TimeRange.Create(AppointmentMother.StartsAt.AddHours(1), AppointmentMother.WindowEnd).Value;

        var result = appointment.RescheduleByPatient(
            appointment.StartsAt,
            EarlyEnough,
            AppointmentMother.PatientRescheduleDeadlineHours,
            AppointmentMother.DurationHours,
            laterWindow);

        result.Error.ShouldBe(AppointmentErrors.TooSoon);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_plazo_que_no_es_positivo_es_un_error_de_programacion(int deadlineHours)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Should.Throw<ArgumentOutOfRangeException>(() => appointment.RescheduleByPatient(
            NewStartsAt, EarlyEnough, deadlineHours, AppointmentMother.DurationHours, AppointmentMother.BookingWindow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_duracion_que_no_es_positiva_es_un_error_de_programacion(int durationHours)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Should.Throw<ArgumentOutOfRangeException>(() => appointment.RescheduleByPatient(
            NewStartsAt,
            EarlyEnough,
            AppointmentMother.PatientRescheduleDeadlineHours,
            durationHours,
            AppointmentMother.BookingWindow));
    }

    [Fact]
    public void Una_ventana_nula_es_un_error_de_programacion()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Should.Throw<ArgumentNullException>(() => appointment.RescheduleByPatient(
            NewStartsAt,
            EarlyEnough,
            AppointmentMother.PatientRescheduleDeadlineHours,
            AppointmentMother.DurationHours,
            null!));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Reprogramar_esta_disponible_para_el_paciente_exactamente_cuando_RescheduleByPatient_tiene_exito(
        int secondsAfterDeadline, bool expected)
    {
        var now = DeadlineInstant.AddSeconds(secondsAfterDeadline);
        var available = AppointmentMother.InStatus(AppointmentStatus.Pending)
            .GetAllowedActions(AppointmentParticipant.Patient, now, AppointmentMother.Rules)
            .Actions.Contains(AppointmentAction.Reschedule);
        var succeeds = Reschedule(AppointmentMother.InStatus(AppointmentStatus.Pending), NewStartsAt, now).IsSuccess;

        available.ShouldBe(expected);
        succeeds.ShouldBe(available);
    }

    private static Result Reschedule(Appointment appointment, DateTimeOffset newStartsAt, DateTimeOffset now) =>
        appointment.RescheduleByPatient(
            newStartsAt,
            now,
            AppointmentMother.PatientRescheduleDeadlineHours,
            AppointmentMother.DurationHours,
            AppointmentMother.BookingWindow);

    // Ante cualquier error la cita conserva su horario y su estado, y no emite eventos.
    private static void AssertRejected(Appointment appointment, Result result, Error error, AppointmentStatus status)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        appointment.Status.ShouldBe(status);
        appointment.StartsAt.ShouldBe(AppointmentMother.StartsAt);
        appointment.EndsAt.ShouldBe(AppointmentMother.StartsAt.AddHours(AppointmentMother.DurationHours));
        appointment.DomainEvents.ShouldBeEmpty();
    }
}
