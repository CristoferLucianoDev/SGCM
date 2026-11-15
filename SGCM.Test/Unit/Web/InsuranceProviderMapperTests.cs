using SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;
using SGCM.Web.Contracts.People;
using SGCM.Web.Mappers.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class InsuranceProviderMapperTests
{
    [Theory]
    [InlineData("  Seguros Universal  ", " 8095550200 ", "Contacto@Seguros.com", " Av. Winston Churchill 100 ", " https://seguros.com ")]
    [InlineData("Seguros Universal", "8095550200", "contacto@seguros.com", "Av. Winston Churchill 100", null)]
    public void ToCommand_registro_copia_los_campos_sin_transformarlos(
        string name, string contactPhone, string contactEmail, string address, string? website)
    {
        var request = new RegisterInsuranceProviderRequest(name, contactPhone, contactEmail, address, website);

        request.ToCommand().ShouldBe(new RegisterInsuranceProviderCommand(name, contactPhone, contactEmail, address, website));
    }
}
