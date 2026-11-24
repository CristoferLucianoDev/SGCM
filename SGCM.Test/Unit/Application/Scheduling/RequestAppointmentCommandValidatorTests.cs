using SGCM.Data.Application.Scheduling.Commands.RequestAppointment;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class RequestAppointmentCommandValidatorTests
{
    private readonly RequestAppointmentCommandValidator _validator = new();

    private static readonly RequestAppointmentCommand Valid = new(
        5, AppointmentMother.StartsAt, AssignedDoctorId: 7, ProvinceId: 3, MunicipalityId: 4, ReasonForVisit: "Control anual");

    [Fact]
    public void Una_solicitud_completa_es_valida()
    {
        _validator.Validate(Valid).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Solo_la_especialidad_y_el_horario_son_obligatorios()
    {
        _validator.Validate(new RequestAppointmentCommand(5, AppointmentMother.StartsAt)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void El_medico_elegido_y_el_asignado_no_van_juntos()
    {
        AssertSingleError(Valid with { DoctorId = 8 }, "Appointment.DoctorAndAssignedDoctor");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void La_especialidad_debe_ser_positiva(int specialtyId)
    {
        AssertSingleError(Valid with { SpecialtyId = specialtyId }, "Appointment.InvalidSpecialtyId");
    }

    [Fact]
    public void Los_identificadores_opcionales_enviados_deben_ser_positivos()
    {
        AssertSingleError(Valid with { AssignedDoctorId = null, DoctorId = 0 }, "Appointment.InvalidDoctorId");
        AssertSingleError(Valid with { AssignedDoctorId = 0 }, "Appointment.InvalidAssignedDoctorId");
        AssertSingleError(Valid with { ProvinceId = 0 }, "Appointment.InvalidProvinceId");
        AssertSingleError(Valid with { MunicipalityId = -2 }, "Appointment.InvalidMunicipalityId");
    }

    [Fact]
    public void Un_motivo_de_500_caracteres_es_valido_aunque_traiga_espacios_alrededor()
    {
        _validator.Validate(Valid with { ReasonForVisit = "  " + new string('A', 500) + "  " }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Un_motivo_de_501_caracteres_se_rechaza()
    {
        AssertSingleError(Valid with { ReasonForVisit = new string('A', 501) }, "Appointment.ReasonTooLong");
    }

    private void AssertSingleError(RequestAppointmentCommand command, string code)
    {
        var result = _validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
    }
}
