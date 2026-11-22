namespace SGCM.Data.Application.People.Queries.ListInsuranceProviders;

/// <summary>
/// Proveedor de seguros tal como lo ve quien consulta el listado.
/// </summary>
/// <param name="Id">Id del proveedor.</param>
/// <param name="Name">Nombre del proveedor.</param>
/// <param name="ContactPhone">Teléfono de contacto.</param>
/// <param name="ContactEmail">Correo de contacto.</param>
/// <param name="Address">Dirección del proveedor.</param>
/// <param name="Website">Sitio web; <c>null</c> si el proveedor no tiene.</param>
/// <param name="IsActive">Estado del proveedor; solo se informa al administrador, para los demás es <c>null</c>.</param>
public sealed record InsuranceProviderDto(
    int Id, string Name, string ContactPhone, string ContactEmail, string Address, string? Website, bool? IsActive);
