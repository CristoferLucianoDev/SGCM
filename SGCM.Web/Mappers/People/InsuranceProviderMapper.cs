using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;
using SGCM.Data.Application.People.Queries.ListInsuranceProviders;
using SGCM.Web.Contracts.People;

namespace SGCM.Web.Mappers.People;

/// <summary>
/// Conversión entre los contratos HTTP de proveedores de seguros y los casos de uso.
/// </summary>
public static class InsuranceProviderMapper
{
    private const string EmptyListMessage = "No existen registros de proveedores de seguros";

    /// <summary>
    /// Copia la solicitud al comando sin transformar los valores: el recorte y la validación
    /// los hace el dominio.
    /// </summary>
    /// <param name="request">Solicitud recibida.</param>
    /// <returns>El comando de registro.</returns>
    public static RegisterInsuranceProviderCommand ToCommand(this RegisterInsuranceProviderRequest request) =>
        new(request.Name, request.ContactPhone, request.ContactEmail, request.Address, request.Website);

    /// <summary>
    /// Convierte los parámetros del listado en la consulta; el estado pasa a un filtro booleano
    /// (<c>null</c> pide todos).
    /// </summary>
    /// <param name="request">Parámetros recibidos.</param>
    /// <returns>La consulta de listado.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el estado no es un valor definido.</exception>
    public static ListInsuranceProvidersQuery ToQuery(this ListInsuranceProvidersRequest request) =>
        new(request.Search, request.Status switch
        {
            InsuranceProviderStatusFilter.Active => true,
            InsuranceProviderStatusFilter.Inactive => false,
            InsuranceProviderStatusFilter.All => (bool?)null,
            _ => throw new ArgumentOutOfRangeException(nameof(request.Status), request.Status, "El estado no es válido.")
        }, request.PageNumber, request.PageSize);

    /// <summary>
    /// Copia el proveedor al contrato HTTP sin modificar sus datos.
    /// </summary>
    /// <param name="provider">Proveedor que devolvió la consulta.</param>
    /// <returns>El contrato de respuesta.</returns>
    public static InsuranceProviderResponse ToResponse(this InsuranceProviderDto provider) =>
        new(provider.Id, provider.Name, provider.ContactPhone, provider.ContactEmail, provider.Address,
            provider.Website, provider.IsActive);

    /// <summary>
    /// Convierte la página al contrato HTTP; solo avisa con un mensaje cuando no hay ningún proveedor.
    /// </summary>
    /// <param name="page">Página que devolvió la consulta.</param>
    /// <returns>El contrato de respuesta con los elementos en el mismo orden.</returns>
    public static ListInsuranceProvidersResponse ToResponse(this PagedResult<InsuranceProviderDto> page) =>
        new(page.Items.Select(provider => provider.ToResponse()).ToArray(),
            page.PageNumber, page.PageSize, page.TotalCount,
            page.TotalCount == 0 ? EmptyListMessage : null);
}
