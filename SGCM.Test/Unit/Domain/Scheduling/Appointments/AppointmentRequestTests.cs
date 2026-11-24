using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

public sealed class AppointmentRequestTests
{
    [Fact]
    public void Solicitar_crea_una_cita_pendiente_en_UTC_con_la_duracion_configurada()
    {
        var result = AppointmentMother.Request();

        result.IsSuccess.ShouldBeTrue();
        var appointment = result.Value;
        appointment.PatientId.ShouldBe(AppointmentMother.PatientId);
        appointment.DoctorId.ShouldBe(AppointmentMother.DoctorId);
        appointment.SpecialtyId.ShouldBe(AppointmentMother.SpecialtyId);
        appointment.StartsAt.ShouldBe(new DateTimeOffset(2027, 1, 12, 13, 0, 0, TimeSpan.Zero));
        appointment.StartsAt.Offset.ShouldBe(TimeSpan.Zero);
        appointment.EndsAt.ShouldBe(new DateTimeOffset(2027, 1, 12, 15, 0, 0, TimeSpan.Zero));
        appointment.Status.ShouldBe(AppointmentStatus.Pending);
        appointment.ReasonForVisit.ShouldBe(AppointmentMother.Reason);
        appointment.StatusReason.ShouldBeNull();
    }

    [Fact]
    public void Solicitar_emite_AppointmentRequestedEvent_con_la_cita()
    {
        var appointment = AppointmentMother.Requested();

        var domainEvent = appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRequestedEvent>();
        domainEvent.Appointment.ShouldBeSameAs(appointment);
    }

    [Fact]
    public void La_duracion_sale_de_la_configuracion()
    {
        var appointment = Appointment.Request(
            AppointmentMother.PatientId, AppointmentMother.DoctorId, AppointmentMother.SpecialtyId,
            AppointmentMother.StartsAt, null, 3, AppointmentMother.BookingWindow).Value;

        appointment.EndsAt.ShouldBe(appointment.StartsAt.AddHours(3));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(1)]
    public void Un_inicio_que_no_esta_en_punto_se_rechaza(int minutes)
    {
        AssertRejected(AppointmentMother.Request(AppointmentMother.StartsAt.AddMinutes(minutes)), "Appointment.NotOnHour");
    }

    [Fact]
    public void Un_inicio_en_punto_con_otro_desfase_tambien_se_acepta()
    {
        var sameInstant = AppointmentMother.StartsAt.ToOffset(TimeSpan.FromHours(2));

        AppointmentMother.Request(sameInstant).Value.StartsAt.ShouldBe(AppointmentMother.StartsAt);
    }

    [Fact]
    public void El_primer_instante_de_la_ventana_se_acepta()
    {
        AppointmentMother.Request(AppointmentMother.WindowStart).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Un_horario_antes_de_la_antelacion_minima_se_rechaza_pidiendo_llamar_al_medico()
    {
        var result = AppointmentMother.Request(AppointmentMother.WindowStart.AddHours(-1));

        AssertRejected(result, "Appointment.TooSoon");
        result.Error.Description.ShouldContain("llamar al médico");
    }

    [Fact]
    public void La_ultima_hora_antes_del_horizonte_se_acepta()
    {
        AppointmentMother.Request(AppointmentMother.WindowEnd.AddHours(-1)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    public void Un_horario_desde_el_fin_del_horizonte_se_rechaza(int hoursAfterEnd)
    {
        AssertRejected(AppointmentMother.Request(AppointmentMother.WindowEnd.AddHours(hoursAfterEnd)), "Appointment.BeyondHorizon");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_motivo_se_guarda_null(string? reason)
    {
        AppointmentMother.Request(reasonForVisit: reason).Value.ReasonForVisit.ShouldBeNull();
    }

    [Fact]
    public void El_motivo_se_guarda_recortado()
    {
        AppointmentMother.Request(reasonForVisit: "  Control anual  ").Value.ReasonForVisit.ShouldBe("Control anual");
    }

    [Fact]
    public void Un_motivo_de_500_caracteres_se_acepta()
    {
        var reason = new string('A', Appointment.ReasonForVisitMaxLength);

        AppointmentMother.Request(reasonForVisit: reason).Value.ReasonForVisit.ShouldBe(reason);
    }

    [Fact]
    public void Un_motivo_de_501_caracteres_se_rechaza()
    {
        var reason = new string('A', Appointment.ReasonForVisitMaxLength + 1);

        AssertRejected(AppointmentMother.Request(reasonForVisit: reason), "Appointment.ReasonTooLong");
    }

    [Theory]
    [InlineData(0, 3, 2, "Appointment.InvalidPatient")]
    [InlineData(-1, 3, 2, "Appointment.InvalidPatient")]
    [InlineData(7, 0, 2, "Appointment.InvalidDoctor")]
    [InlineData(7, 3, 0, "Appointment.InvalidSpecialty")]
    public void Los_identificadores_deben_ser_positivos(int patientId, int doctorId, int specialtyId, string code)
    {
        AssertRejected(
            AppointmentMother.Request(patientId: patientId, doctorId: doctorId, specialtyId: specialtyId), code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Una_duracion_no_positiva_es_un_error_de_programacion(int hours)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Appointment.Request(
            AppointmentMother.PatientId, AppointmentMother.DoctorId, AppointmentMother.SpecialtyId,
            AppointmentMother.StartsAt, null, hours, AppointmentMother.BookingWindow));
    }

    [Fact]
    public void Una_ventana_nula_es_un_error_de_programacion()
    {
        Should.Throw<ArgumentNullException>(() => Appointment.Request(
            AppointmentMother.PatientId, AppointmentMother.DoctorId, AppointmentMother.SpecialtyId,
            AppointmentMother.StartsAt, null, AppointmentMother.DurationHours, null!));
    }

    private static void AssertRejected(Result<Appointment> result, string code)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }
}
