using SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;
using SGCM.Web.Contracts.Clinical;
using SGCM.Web.Mappers.Clinical;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class MedicalRecordMapperTests
{
    [Fact]
    public void ToCommand_lleva_la_cita_el_contenido_y_la_ip()
    {
        var request = new CreateMedicalRecordVersionRequest("Faringitis", "Reposo", "Control");

        request.ToCommand(99, "10.0.0.5").ShouldBe(
            new CreateMedicalRecordVersionCommand(99, "Faringitis", "Reposo", "Control", "10.0.0.5"));
    }

    [Fact]
    public void ToResponse_copia_todos_los_campos()
    {
        var createdAt = new DateTimeOffset(2027, 1, 12, 13, 30, 0, TimeSpan.Zero);
        var result = new MedicalRecordVersionResult(501, 99, "Faringitis", null, "Control", createdAt, IsCorrection: true);

        result.ToResponse().ShouldBe(
            new MedicalRecordVersionResponse(501, 99, "Faringitis", null, "Control", createdAt, IsCorrection: true));
    }

    [Fact]
    public void Los_mapeos_rechazan_nulos()
    {
        Should.Throw<ArgumentNullException>(() => ((CreateMedicalRecordVersionRequest)null!).ToCommand(99, null));
        Should.Throw<ArgumentNullException>(() => ((MedicalRecordVersionResult)null!).ToResponse());
    }
}
