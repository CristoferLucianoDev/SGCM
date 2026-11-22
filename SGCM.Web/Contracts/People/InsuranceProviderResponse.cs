using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.People;

/// <summary>
/// Proveedor de seguros del listado.
/// </summary>
/// <param name="Id">Id del proveedor.</param>
/// <param name="Name">Nombre del proveedor.</param>
/// <param name="ContactPhone">Teléfono de contacto.</param>
/// <param name="ContactEmail">Correo de contacto.</param>
/// <param name="Address">Dirección del proveedor.</param>
/// <param name="Website">Sitio web; se serializa como <c>null</c> si el proveedor no tiene.</param>
/// <param name="IsActive">Estado del proveedor; solo se incluye para el administrador y se omite si es <c>null</c>.</param>
public sealed record InsuranceProviderResponse(
    int Id,
    string Name,
    string ContactPhone,
    string ContactEmail,
    string Address,
    string? Website,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsActive);
