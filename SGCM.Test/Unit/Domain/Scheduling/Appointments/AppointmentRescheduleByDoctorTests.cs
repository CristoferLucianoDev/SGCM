using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

/// <summary>
/// Pruebas de <see cref="Appointment.RescheduleByDoctor"/>: estado, cita ya empezada, hora futura, ventana del médico,
/// mismo inicio y coherencia con las acciones permitidas del médico.
/// </summary>
public sealed class AppointmentRescheduleByDoctorTests
{
    private const int ThreeDaysHours = 72;
    private const int HoursBeforeStartInsideDoctorReach = 23;
    private const int HoursIntoTheAppointment = 1;
    private const int HalfHourMinutes = 30;
    private const int HoursBeforeStartPastConfirmationDeadline = 47;

    private static readonly DateTimeOffset Now = AppointmentMother.StartsAt.AddHours(-ThreeDaysHours);

    // Miércoles 20 de enero de 2027 a las 10:00 en Santo Domingo: en punto y dentro de la ventana del médico.
    private static readonly DateTimeOffset NewStartsAt = new(2027, 1, 20, 10, 0, 0, AppointmentMother.SantoDomingo);

    private static readonly DateTimeOffset OriginalEndsAt =
        AppointmentMother.StartsAt.AddHours(AppointmentMother.DurationHours);

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    public void Reprogramar_una_cita_activa_la_deja_confirmada_con_el_nuevo_horario_en_UTC_y_la_duracion(
        AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = Reschedule(appointment, NewStartsAt, Now);

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Confirmed);
        appointment.StartsAt.ShouldBe(NewStartsAt);
        appointment.StartsAt.Offset.ShouldBe(TimeSpan.Zero);
        appointment.EndsAt.ShouldBe(NewStartsAt.AddHours(AppointmentMother.DurationHours));
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    public void Reprogramar_emite_un_unico_AppointmentRescheduledEvent_con_el_medico_y_el_inicio_anterior(
        AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);
        var previousStartsAt = appointment.StartsAt;

        Reschedule(appointment, NewStartsAt, Now);

        var domainEvent = appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRescheduledEvent>();
        domainEvent.Appointment.ShouldBeSameAs(appointment);
        domainEvent.RescheduledBy.ShouldBe(AppointmentParticipant.Doctor);
        domainEvent.PreviousStartsAt.ShouldBe(previousStartsAt);
    }

    [Fact]
    public void Puede_mover_la_cita_a_la_misma_hora_del_dia_siguiente()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        var tomorrow = Now.AddDays(1);

        var result = Reschedule(appointment, tomorrow, Now);

        result.IsSuccess.ShouldBeTrue();
        appointment.StartsAt.ShouldBe(tomorrow);
    }

    [Fact]
    public void Puede_mover_la_cita_a_una_hora_en_el_futuro()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        var inOneHour = Now.AddHours(1);

        var result = Reschedule(appointment, inOneHour, Now);

        result.IsSuccess.ShouldBeTrue();
        appointment.StartsAt.ShouldBe(inOneHour);
    }

    [Fact]
    public void A_23_horas_del_inicio_tambien_puede_reprogramar_porque_no_le_aplica_el_plazo_del_paciente()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        var now = AppointmentMother.StartsAt.AddHours(-HoursBeforeStartInsideDoctorReach);

        var result = Reschedule(appointment, NewStartsAt, now);

        result.IsSuccess.ShouldBeTrue();
        appointment.StartsAt.ShouldBe(NewStartsAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Una_nueva_hora_en_el_pasado_o_en_el_instante_actual_es_NewStartNotInFuture_y_nunca_TooSoon(
        int hoursFromNow)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = Reschedule(appointment, Now.AddHours(hoursFromNow), Now);

        AssertRejected(appointment, result, AppointmentErrors.NewStartNotInFuture, AppointmentStatus.Confirmed);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Una_hora_que_no_esta_en_punto_es_NotOnHour()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = Reschedule(appointment, NewStartsAt.AddMinutes(HalfHourMinutes), Now);

        AssertRejected(appointment, result, AppointmentErrors.NotOnHour, AppointmentStatus.Confirmed);
    }

    [Fact]
    public void Una_hora_desde_el_fin_del_horizonte_es_BeyondHorizon()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = Reschedule(appointment, AppointmentMother.WindowEnd, Now);

        AssertRejected(appointment, result, AppointmentErrors.BeyondHorizon, AppointmentStatus.Confirmed);
    }

    [Fact]
    public void El_mismo_inicio_es_SameSchedule()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = Reschedule(appointment, appointment.StartsAt, Now);

        AssertRejected(appointment, result, AppointmentErrors.SameSchedule, AppointmentStatus.Confirmed);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void El_mismo_inicio_con_otro_desfase_tambien_es_SameSchedule()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reschedule(appointment, appointment.StartsAt.ToOffset(TimeSpan.Zero), Now);

        result.Error.ShouldBe(AppointmentErrors.SameSchedule);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(HoursIntoTheAppointment)]
    public void Una_cita_que_ya_empezo_es_AlreadyStarted(int hoursAfterStart)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        var now = AppointmentMother.StartsAt.AddHours(hoursAfterStart);

        var result = Reschedule(appointment, NewStartsAt, now);

        AssertRejected(appointment, result, AppointmentErrors.AlreadyStarted, AppointmentStatus.Confirmed);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Una_cita_pendiente_que_ya_empezo_es_AlreadyStarted()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Reschedule(appointment, NewStartsAt, AppointmentMother.StartsAt);

        AssertRejected(appointment, result, AppointmentErrors.AlreadyStarted, AppointmentStatus.Pending);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Una_cita_pendiente_con_el_plazo_de_confirmacion_vencido_queda_confirmada_en_el_nuevo_horario()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
        var now = AppointmentMother.StartsAt.AddHours(-HoursBeforeStartPastConfirmationDeadline);

        var result = Reschedule(appointment, NewStartsAt, now);

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Confirmed);
        appointment.StartsAt.ShouldBe(NewStartsAt);
        appointment.EndsAt.ShouldBe(NewStartsAt.AddHours(AppointmentMother.DurationHours));
    }

    [Theory]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public void Una_cita_en_estado_final_es_NotActive(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = Reschedule(appointment, NewStartsAt, Now);

        AssertRejected(appointment, result, AppointmentErrors.NotActive, status);
    }

    [Fact]
    public void El_estado_se_valida_antes_que_si_ya_empezo()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Cancelled);

        var result = Reschedule(appointment, NewStartsAt, AppointmentMother.StartsAt);

        result.Error.ShouldBe(AppointmentErrors.NotActive);
    }

    [Fact]
    public void Si_ya_empezo_se_informa_antes_que_la_hora_futura()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        var now = AppointmentMother.StartsAt;

        var result = Reschedule(appointment, now.AddHours(-1), now);

        result.Error.ShouldBe(AppointmentErrors.AlreadyStarted);
    }

    [Fact]
    public void La_hora_futura_se_valida_antes_que_la_ventana()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        // Pasada y fuera de punto a la vez: se informa que no es futura, no que no está en punto.
        var result = Reschedule(appointment, Now.AddMinutes(-HalfHourMinutes), Now);

        result.Error.ShouldBe(AppointmentErrors.NewStartNotInFuture);
    }

    [Fact]
    public void La_ventana_se_valida_antes_que_el_mismo_inicio()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        // Ventana que termina en el inicio de la cita: su propio inicio queda fuera y se informa BeyondHorizon.
        var earlyWindow = TimeRange.Create(Now, appointment.StartsAt).Value;

        var result = appointment.RescheduleByDoctor(
            appointment.StartsAt, Now, AppointmentMother.DurationHours, earlyWindow);

        result.Error.ShouldBe(AppointmentErrors.BeyondHorizon);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_duracion_que_no_es_positiva_es_un_error_de_programacion(int durationHours)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        Should.Throw<ArgumentOutOfRangeException>(() => appointment.RescheduleByDoctor(
            NewStartsAt, Now, durationHours, DoctorWindow(Now)));
    }

    [Fact]
    public void Una_ventana_nula_es_un_error_de_programacion()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        Should.Throw<ArgumentNullException>(() => appointment.RescheduleByDoctor(
            NewStartsAt, Now, AppointmentMother.DurationHours, null!));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    public void Reprogramar_esta_disponible_para_el_medico_exactamente_cuando_RescheduleByDoctor_tiene_exito(
        int secondsFromStart, bool expected)
    {
        var now = AppointmentMother.StartsAt.AddSeconds(secondsFromStart);
        var available = AppointmentMother.InStatus(AppointmentStatus.Confirmed)
            .GetAllowedActions(AppointmentParticipant.Doctor, now, AppointmentMother.Rules)
            .Actions.Contains(AppointmentAction.Reschedule);
        var succeeds = Reschedule(AppointmentMother.InStatus(AppointmentStatus.Confirmed), NewStartsAt, now).IsSuccess;

        available.ShouldBe(expected);
        succeeds.ShouldBe(available);
    }

    // La ventana del médico empieza en el instante actual, sin antelación mínima, y termina donde la del paciente.
    private static TimeRange DoctorWindow(DateTimeOffset now) => TimeRange.Create(now, AppointmentMother.WindowEnd).Value;

    private static Result Reschedule(Appointment appointment, DateTimeOffset newStartsAt, DateTimeOffset now) =>
        appointment.RescheduleByDoctor(newStartsAt, now, AppointmentMother.DurationHours, DoctorWindow(now));

    // Ante cualquier error la cita conserva su horario y su estado, y no emite eventos.
    private static void AssertRejected(Appointment appointment, Result result, Error error, AppointmentStatus status)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        appointment.Status.ShouldBe(status);
        appointment.StartsAt.ShouldBe(AppointmentMother.StartsAt);
        appointment.EndsAt.ShouldBe(OriginalEndsAt);
        appointment.DomainEvents.ShouldBeEmpty();
    }
}
