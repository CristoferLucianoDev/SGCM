using SGCM.Data.Application.Clinical.Queries.GetMedicalHistory;
using Shouldly;

namespace SGCM.Test.Unit.Application.Clinical;

/// <summary>Pruebas de <see cref="GetMedicalHistoryQueryValidator"/>.</summary>
public sealed class GetMedicalHistoryQueryValidatorTests
{
    private readonly GetMedicalHistoryQueryValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void Acepta_el_historial_propio_y_un_paciente_valido(int? patientId)
    {
        _validator.Validate(new GetMedicalHistoryQuery(patientId, IpAddress: null)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rechaza_un_id_de_paciente_no_positivo(int patientId)
    {
        _validator.Validate(new GetMedicalHistoryQuery(patientId, IpAddress: null))
            .Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("MedicalHistory.InvalidPatientId");
    }
}
