using SGCM.Data.Application.Scheduling.Commands.RejectAppointment;
using SGCM.Data.Domain.Scheduling.Appointments;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class RejectAppointmentCommandValidatorTests
{
    private const int AppointmentId = 99;
    private const string Reason = "Agenda completa";

    private readonly RejectAppointmentCommandValidator _validator = new();

    [Fact]
    public void Un_comando_con_id_positivo_y_motivo_es_valido()
    {
        _validator.Validate(new RejectAppointmentCommand(AppointmentId, Reason)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_id_de_la_cita_debe_ser_positivo(int appointmentId)
    {
        AssertSingleError(new RejectAppointmentCommand(appointmentId, Reason), "Appointment.InvalidAppointmentId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void El_motivo_es_obligatorio(string? reason)
    {
        AssertSingleError(new RejectAppointmentCommand(AppointmentId, reason), "Appointment.StatusReasonRequired");
    }

    [Fact]
    public void Un_motivo_de_la_longitud_maxima_es_valido()
    {
        var reason = new string('a', Appointment.StatusReasonMaxLength);

        _validator.Validate(new RejectAppointmentCommand(AppointmentId, reason)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Un_motivo_demasiado_largo_se_rechaza()
    {
        var reason = new string('a', Appointment.StatusReasonMaxLength + 1);

        AssertSingleError(new RejectAppointmentCommand(AppointmentId, reason), "Appointment.StatusReasonTooLong");
    }

    [Fact]
    public void El_motivo_se_mide_recortado()
    {
        var reason = $"  {new string('a', Appointment.StatusReasonMaxLength)}  ";

        _validator.Validate(new RejectAppointmentCommand(AppointmentId, reason)).IsValid.ShouldBeTrue();
    }

    private void AssertSingleError(RejectAppointmentCommand command, string code)
    {
        var result = _validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
    }
}
