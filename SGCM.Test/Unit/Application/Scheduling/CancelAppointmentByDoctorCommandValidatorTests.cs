using SGCM.Data.Application.Scheduling.Commands.CancelAppointmentByDoctor;
using SGCM.Data.Domain.Scheduling.Appointments;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class CancelAppointmentByDoctorCommandValidatorTests
{
    private const int AppointmentId = 99;
    private const string Reason = "Emergencia familiar";

    private readonly CancelAppointmentByDoctorCommandValidator _validator = new();

    [Fact]
    public void Un_comando_con_id_positivo_y_motivo_es_valido()
    {
        _validator.Validate(new CancelAppointmentByDoctorCommand(AppointmentId, Reason)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_id_de_la_cita_debe_ser_positivo(int appointmentId)
    {
        AssertSingleError(new CancelAppointmentByDoctorCommand(appointmentId, Reason), "Appointment.InvalidAppointmentId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void El_motivo_es_obligatorio(string? reason)
    {
        AssertSingleError(new CancelAppointmentByDoctorCommand(AppointmentId, reason), "Appointment.StatusReasonRequired");
    }

    [Fact]
    public void Un_motivo_de_la_longitud_maxima_es_valido()
    {
        var reason = new string('a', Appointment.StatusReasonMaxLength);

        _validator.Validate(new CancelAppointmentByDoctorCommand(AppointmentId, reason)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Un_motivo_demasiado_largo_se_rechaza()
    {
        var reason = new string('a', Appointment.StatusReasonMaxLength + 1);

        AssertSingleError(new CancelAppointmentByDoctorCommand(AppointmentId, reason), "Appointment.StatusReasonTooLong");
    }

    [Fact]
    public void El_motivo_se_mide_recortado()
    {
        var reason = $"  {new string('a', Appointment.StatusReasonMaxLength)}  ";

        _validator.Validate(new CancelAppointmentByDoctorCommand(AppointmentId, reason)).IsValid.ShouldBeTrue();
    }

    private void AssertSingleError(CancelAppointmentByDoctorCommand command, string code)
    {
        var result = _validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
    }
}
