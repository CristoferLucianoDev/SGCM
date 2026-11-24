using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

public sealed class AppointmentCancelTests
{
    private static readonly DateTimeOffset EarlyEnough = AppointmentMother.StartsAt.AddHours(-48);

    private static readonly DateTimeOffset DeadlineInstant =
        AppointmentMother.StartsAt.AddHours(-AppointmentMother.PatientCancellationDeadlineHours);

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    public void Cancelar_una_cita_activa_la_deja_cancelada_con_el_motivo(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = Cancel(appointment, "Tengo un viaje", EarlyEnough);

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
        appointment.StatusReason.ShouldBe("Tengo un viaje");
    }

    [Fact]
    public void Cancelar_emite_un_unico_AppointmentCancelledEvent_con_la_cita_y_quien_cancela()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Cancel(appointment, "Tengo un viaje", EarlyEnough);

        var domainEvent = appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentCancelledEvent>();
        domainEvent.Appointment.ShouldBeSameAs(appointment);
        domainEvent.CancelledBy.ShouldBe(AppointmentCanceller.Patient);
    }

    [Theory]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public void Una_cita_que_no_esta_activa_no_se_puede_cancelar(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = Cancel(appointment, "Tengo un viaje", EarlyEnough);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AppointmentErrors.InvalidTransition);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        appointment.Status.ShouldBe(status);
        appointment.StatusReason.ShouldBeNull();
        appointment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Cancelar_justo_en_el_limite_del_plazo_se_permite()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Cancel(appointment, null, DeadlineInstant);

        result.IsSuccess.ShouldBeTrue();
        appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
    }

    [Theory]
    [MemberData(nameof(InstantsAfterDeadline))]
    public void Cancelar_fuera_del_plazo_se_rechaza(DateTimeOffset now)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Cancel(appointment, "Tengo un viaje", now);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AppointmentErrors.CancellationDeadlinePassed);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Description.ShouldContain("llamar al médico");
        appointment.Status.ShouldBe(AppointmentStatus.Pending);
        appointment.StatusReason.ShouldBeNull();
        appointment.DomainEvents.ShouldBeEmpty();
    }

    public static TheoryData<DateTimeOffset> InstantsAfterDeadline => new()
    {
        DeadlineInstant.AddSeconds(1),
        AppointmentMother.StartsAt.AddHours(-1),
        AppointmentMother.StartsAt.AddHours(1)
    };

    [Fact]
    public void El_estado_se_valida_antes_que_el_plazo()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Completed);

        var result = Cancel(appointment, null, AppointmentMother.StartsAt.AddHours(1));

        result.Error.ShouldBe(AppointmentErrors.InvalidTransition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_motivo_vacio_se_guarda_como_nulo(string? reason)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        var result = Cancel(appointment, reason, EarlyEnough);

        result.IsSuccess.ShouldBeTrue();
        appointment.StatusReason.ShouldBeNull();
    }

    [Fact]
    public void El_motivo_se_guarda_recortado()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Cancel(appointment, "  Tengo un viaje  ", EarlyEnough);

        appointment.StatusReason.ShouldBe("Tengo un viaje");
    }

    [Fact]
    public void Un_motivo_de_la_longitud_maxima_se_acepta()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
        var reason = new string('a', Appointment.StatusReasonMaxLength);

        var result = Cancel(appointment, reason, EarlyEnough);

        result.IsSuccess.ShouldBeTrue();
        appointment.StatusReason.ShouldBe(reason);
    }

    [Fact]
    public void Un_motivo_demasiado_largo_se_rechaza_y_la_cita_no_cambia()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
        var reason = new string('a', Appointment.StatusReasonMaxLength + 1);

        var result = Cancel(appointment, reason, EarlyEnough);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AppointmentErrors.StatusReasonTooLong);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        appointment.Status.ShouldBe(AppointmentStatus.Pending);
        appointment.StatusReason.ShouldBeNull();
        appointment.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_plazo_que_no_es_positivo_es_un_error_de_programacion(int deadlineHours)
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Should.Throw<ArgumentOutOfRangeException>(() =>
            appointment.Cancel(AppointmentCanceller.Patient, null, EarlyEnough, deadlineHours));
    }

    [Fact]
    public void Quien_cancela_desconocido_es_un_error_de_programacion()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);

        Should.Throw<ArgumentOutOfRangeException>(() =>
            appointment.Cancel(
                (AppointmentCanceller)99, null, EarlyEnough, AppointmentMother.PatientCancellationDeadlineHours));
    }

    private static Result Cancel(Appointment appointment, string? reason, DateTimeOffset now) =>
        appointment.Cancel(
            AppointmentCanceller.Patient, reason, now, AppointmentMother.PatientCancellationDeadlineHours);
}
