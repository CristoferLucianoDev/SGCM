using SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;
using SGCM.Web.Contracts.People;

namespace SGCM.Web.Mappers.People;

/// <summary>
/// Conversión entre los contratos HTTP de proveedores de seguros y los casos de uso.
/// </summary>
public static class InsuranceProviderMapper
{
    /// <summary>
    /// Copia la solicitud al comando sin transformar los valores: el recorte y la validación
    /// los hace el dominio.
    /// </summary>
    /// <param name="request">Solicitud recibida.</param>
    /// <returns>El comando de registro.</returns>
    public static RegisterInsuranceProviderCommand ToCommand(this RegisterInsuranceProviderRequest request) =>
        new(request.Name, request.ContactPhone, request.ContactEmail, request.Address, request.Website);
}
