using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.People.InsuranceProviders;

/// <summary>
/// Cambios parciales de un <see cref="InsuranceProvider"/>. Cada campo no enviado conserva su valor;
/// el valor por defecto de la estructura no envía ninguno.
/// </summary>
/// <param name="Name">Cambio del nombre.</param>
/// <param name="ContactPhone">Cambio del teléfono de contacto.</param>
/// <param name="ContactEmail">Cambio del correo de contacto.</param>
/// <param name="Address">Cambio de la dirección.</param>
/// <param name="Website">Cambio del sitio web; enviarlo nulo o vacío lo quita.</param>
public readonly record struct InsuranceProviderChanges(
    Change<string?> Name,
    Change<string?> ContactPhone,
    Change<string?> ContactEmail,
    Change<string?> Address,
    Change<string?> Website)
{
    /// <summary>Indica si se envió al menos un campo.</summary>
    public bool HasChanges =>
        Name.IsProvided || ContactPhone.IsProvided || ContactEmail.IsProvided || Address.IsProvided || Website.IsProvided;
}
