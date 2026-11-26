using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetPatientAppointmentDetailQueryValidatorTests
{
    private const int ValidAppointmentId = 7;

    private readonly GetPatientAppointmentDetailQueryValidator _validator = new();

    [Fact]
    public void Un_id_positivo_es_valido()
    {
        _validator.Validate(new GetPatientAppointmentDetailQuery(ValidAppointmentId)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_id_de_la_cita_debe_ser_mayor_que_cero(int appointmentId)
    {
        var result = _validator.Validate(new GetPatientAppointmentDetailQuery(appointmentId));

        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("Appointment.InvalidAppointmentId");
    }
}
