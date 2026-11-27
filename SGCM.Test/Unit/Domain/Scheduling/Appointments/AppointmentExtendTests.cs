using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

/// <summary>Pruebas de <see cref="Appointment.Extend"/>: estado, horario de la extensión y coherencia con las acciones.</summary>
public sealed class AppointmentExtendTests
{
    private const int ExtraHours = 1;
    private const int TwoExtraHours = 2;
    private const int HoursIntoTheAppointment = 1;

    private static readonly DateTimeOffset StartsAt = AppointmentMother.StartsAt;
    private static readonly DateTimeOffset EndsAt = StartsAt.AddHours(AppointmentMother.DurationHours);
    private static readonly DateTimeOffset InProgressNow = StartsAt.AddHours(HoursIntoTheAppointment);

    private static readonly IReadOnlyCollection<TimeRange> NoAppointments = [];

    [Fact]
    public void Extender_una_cita_en_curso_con_bloque_suficiente_avanza_el_fin_y_no_emite_eventos()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.Extend(ExtraHours, InProgressNow, [Range(StartsAt, EndsAt.AddHours(ExtraHours))], NoAppointments);

        result.IsSuccess.ShouldBeTrue();
        appointment.EndsAt.ShouldBe(EndsAt.AddHours(ExtraHours));
        appointment.Status.ShouldBe(AppointmentStatus.Confirmed);
        appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Una_extension_que_sale_del_bloque_se_rechaza_y_el_fin_no_cambia()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.Extend(ExtraHours, InProgressNow, [Range(StartsAt, EndsAt)], NoAppointments);

        AssertExtensionUnavailable(appointment, result);
    }

    [Fact]
    public void Una_extension_que_solo_cabe_uniendo_bloques_contiguos_se_rechaza()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        // La base exige que la cita completa quepa en un solo bloque (trigger T3): el tramo nuevo solo no basta.
        TimeRange[] blocks = [Range(StartsAt, EndsAt), Range(EndsAt, EndsAt.AddHours(TwoExtraHours))];

        var result = appointment.Extend(TwoExtraHours, InProgressNow, blocks, NoAppointments);

        AssertExtensionUnavailable(appointment, result);
    }

    [Fact]
    public void Un_bloque_que_cubre_solo_el_tramo_nuevo_y_no_el_inicio_de_la_cita_no_alcanza()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.Extend(
            TwoExtraHours, InProgressNow, [Range(EndsAt, EndsAt.AddHours(TwoExtraHours))], NoAppointments);

        AssertExtensionUnavailable(appointment, result);
    }

    [Fact]
    public void Una_cita_activa_que_empieza_en_el_fin_actual_impide_extender()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        TimeRange[] others = [Range(EndsAt, EndsAt.AddHours(AppointmentMother.DurationHours))];

        var result = appointment.Extend(ExtraHours, InProgressNow, [Range(StartsAt, EndsAt.AddHours(TwoExtraHours))], others);

        AssertExtensionUnavailable(appointment, result);
    }

    [Fact]
    public void Una_cita_ajena_que_empieza_antes_del_fin_actual_y_cruza_el_tramo_impide_extender()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        TimeRange[] others = [Range(StartsAt.AddHours(HoursIntoTheAppointment), EndsAt.AddHours(ExtraHours))];

        var result = appointment.Extend(ExtraHours, InProgressNow, [Range(StartsAt, EndsAt.AddHours(ExtraHours))], others);

        AssertExtensionUnavailable(appointment, result);
    }

    [Fact]
    public void Una_cita_activa_que_empieza_justo_en_el_nuevo_fin_no_impide_extender()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        var newEnd = EndsAt.AddHours(ExtraHours);
        TimeRange[] others = [Range(newEnd, newEnd.AddHours(AppointmentMother.DurationHours))];

        var result = appointment.Extend(ExtraHours, InProgressNow, [Range(StartsAt, newEnd.AddHours(TwoExtraHours))], others);

        result.IsSuccess.ShouldBeTrue();
        appointment.EndsAt.ShouldBe(newEnd);
    }

    [Fact]
    public void Extender_antes_del_inicio_es_NotInProgress()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.Extend(
            ExtraHours, StartsAt.AddSeconds(-1), [Range(StartsAt, EndsAt.AddHours(TwoExtraHours))], NoAppointments);

        AssertNotInProgress(appointment, result, AppointmentStatus.Confirmed);
    }

    [Fact]
    public void Extender_un_segundo_antes_del_fin_sigue_permitido()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);
        var oneSecondBeforeEnd = EndsAt.AddSeconds(-1);

        var result = appointment.Extend(
            ExtraHours, oneSecondBeforeEnd, [Range(StartsAt, EndsAt.AddHours(ExtraHours))], NoAppointments);

        result.IsSuccess.ShouldBeTrue();
        appointment.EndsAt.ShouldBe(EndsAt.AddHours(ExtraHours));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Extender_en_el_fin_exacto_o_despues_es_NotInProgress(int secondsAfterEnd)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.Extend(
            ExtraHours, EndsAt.AddSeconds(secondsAfterEnd), [Range(StartsAt, EndsAt.AddHours(TwoExtraHours))], NoAppointments);

        AssertNotInProgress(appointment, result, AppointmentStatus.Confirmed);
    }

    [Fact]
    public void Extender_una_cita_pendiente_es_NotInProgress()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = appointment.Extend(ExtraHours, InProgressNow, [Range(StartsAt, EndsAt.AddHours(TwoExtraHours))], NoAppointments);

        AssertNotInProgress(appointment, result, AppointmentStatus.Pending);
    }

    [Theory]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public void Extender_una_cita_en_estado_final_es_NotInProgress(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = appointment.Extend(ExtraHours, InProgressNow, [Range(StartsAt, EndsAt.AddHours(TwoExtraHours))], NoAppointments);

        AssertNotInProgress(appointment, result, status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Unas_horas_que_no_son_positivas_son_un_error_de_programacion(int additionalHours)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        Should.Throw<ArgumentOutOfRangeException>(
            () => appointment.Extend(additionalHours, InProgressNow, [Range(StartsAt, EndsAt)], NoAppointments));
    }

    [Fact]
    public void Los_bloques_nulos_son_un_error_de_programacion()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        Should.Throw<ArgumentNullException>(() => appointment.Extend(ExtraHours, InProgressNow, null!, NoAppointments));
    }

    [Fact]
    public void Las_citas_nulas_son_un_error_de_programacion()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        Should.Throw<ArgumentNullException>(
            () => appointment.Extend(ExtraHours, InProgressNow, [Range(StartsAt, EndsAt)], null!));
    }

    [Fact]
    public void En_curso_el_medico_tiene_Extend_entre_sus_acciones_permitidas()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var allowed = appointment.GetAllowedActions(AppointmentParticipant.Doctor, InProgressNow, AppointmentMother.Rules);

        allowed.Actions.ShouldContain(AppointmentAction.Extend);
    }

    private static TimeRange Range(DateTimeOffset start, DateTimeOffset end) => TimeRange.Create(start, end).Value;

    private static void AssertExtensionUnavailable(Appointment appointment, Result result)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AppointmentErrors.ExtensionUnavailable);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        appointment.EndsAt.ShouldBe(EndsAt);
        appointment.DomainEvents.ShouldBeEmpty();
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
