using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

/// <summary>
/// Pruebas de <see cref="Appointment.EnsureClinicalRecordAllowed"/>: el médico de la cita registra información
/// clínica desde la hora de inicio de una cita confirmada o completada.
/// </summary>
public sealed class AppointmentClinicalRecordTests
{
    private const int DoctorId = AppointmentMother.DoctorId;
    private const int OtherDoctorId = AppointmentMother.DoctorId + 1;
    private const int HoursIntoTheAppointment = 1;

    private static readonly DateTimeOffset StartsAt = AppointmentMother.StartsAt;
    private static readonly DateTimeOffset AfterTheEnd = StartsAt.AddHours(AppointmentMother.DurationHours + 1);

    [Fact]
    public void Se_permite_desde_la_hora_exacta_de_inicio_de_una_cita_confirmada()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        appointment.EnsureClinicalRecordAllowed(DoctorId, StartsAt).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Se_permite_mientras_la_cita_confirmada_esta_en_curso()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        appointment.EnsureClinicalRecordAllowed(DoctorId, StartsAt.AddHours(HoursIntoTheAppointment))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Se_permite_en_una_cita_confirmada_que_ya_paso_su_fin_y_aun_no_se_completo()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        appointment.EnsureClinicalRecordAllowed(DoctorId, AfterTheEnd).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Se_permite_en_una_cita_completada()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Completed);

        appointment.EnsureClinicalRecordAllowed(DoctorId, AfterTheEnd).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Una_cita_confirmada_que_aun_no_empieza_no_lo_admite()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        var result = appointment.EnsureClinicalRecordAllowed(DoctorId, StartsAt.AddSeconds(-1));

        result.Error.ShouldBe(AppointmentErrors.ClinicalRecordNotStarted);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Rejected)]
    public void Una_cita_pendiente_cancelada_o_rechazada_no_lo_admite_aunque_ya_haya_empezado(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = appointment.EnsureClinicalRecordAllowed(DoctorId, AfterTheEnd);

        result.Error.ShouldBe(AppointmentErrors.ClinicalRecordInvalidStatus);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void La_cita_de_otro_medico_no_lo_admite_y_se_comprueba_antes_que_el_estado(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);

        var result = appointment.EnsureClinicalRecordAllowed(OtherDoctorId, AfterTheEnd);

        result.Error.ShouldBe(AppointmentErrors.ClinicalRecordDoctorOnly);
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
    }

    [Fact]
    public void El_estado_se_comprueba_antes_que_la_hora_de_inicio()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Cancelled);

        appointment.EnsureClinicalRecordAllowed(DoctorId, StartsAt.AddHours(-HoursIntoTheAppointment))
            .Error.ShouldBe(AppointmentErrors.ClinicalRecordInvalidStatus);
    }

    [Fact]
    public void Comprobar_la_regla_no_cambia_la_cita_ni_emite_eventos()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Confirmed);

        appointment.EnsureClinicalRecordAllowed(DoctorId, StartsAt);

        appointment.Status.ShouldBe(AppointmentStatus.Confirmed);
        appointment.StartsAt.ShouldBe(StartsAt);
        appointment.EndsAt.ShouldBe(StartsAt.AddHours(AppointmentMother.DurationHours));
        appointment.DomainEvents.ShouldBeEmpty();
    }
}
