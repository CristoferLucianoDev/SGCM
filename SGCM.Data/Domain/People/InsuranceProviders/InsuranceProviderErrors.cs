using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.People.InsuranceProviders;

/// <summary>
/// Errores de negocio del agregado <see cref="InsuranceProvider"/>. Cada descripción nombra
/// el campo, porque la API muestra solo el primer error encontrado.
/// </summary>
public static class InsuranceProviderErrors
{
    /// <summary>Ya existe un proveedor, activo o inactivo, con el mismo nombre.</summary>
    public static readonly Error NameAlreadyInUse = Error.Conflict(
        code: "InsuranceProvider.NameAlreadyInUse",
        description: "Ya existe un proveedor de seguros con ese nombre."
    );

    /// <summary>Falta el nombre.</summary>
    public static readonly Error NameRequired = Error.Validation(
        code: "InsuranceProvider.NameRequired",
        description: "El nombre del proveedor de seguros es obligatorio."
    );

    /// <summary>El nombre supera la longitud máxima.</summary>
    public static readonly Error NameTooLong = Error.Validation(
        code: "InsuranceProvider.NameTooLong",
        description: $"El nombre del proveedor de seguros no puede superar {InsuranceProvider.MaxNameLength} caracteres."
    );

    /// <summary>Falta el teléfono de contacto.</summary>
    public static readonly Error ContactPhoneRequired = Error.Validation(
        code: "InsuranceProvider.ContactPhoneRequired",
        description: "El teléfono de contacto es obligatorio."
    );

    /// <summary>El teléfono de contacto supera la longitud máxima.</summary>
    public static readonly Error ContactPhoneTooLong = Error.Validation(
        code: "InsuranceProvider.ContactPhoneTooLong",
        description: $"El teléfono de contacto no puede superar {InsuranceProvider.MaxContactPhoneLength} caracteres."
    );

    /// <summary>Falta la dirección.</summary>
    public static readonly Error AddressRequired = Error.Validation(
        code: "InsuranceProvider.AddressRequired",
        description: "La dirección es obligatoria."
    );

    /// <summary>La dirección supera la longitud máxima.</summary>
    public static readonly Error AddressTooLong = Error.Validation(
        code: "InsuranceProvider.AddressTooLong",
        description: $"La dirección no puede superar {InsuranceProvider.MaxAddressLength} caracteres."
    );

    /// <summary>El sitio web supera la longitud máxima.</summary>
    public static readonly Error WebsiteTooLong = Error.Validation(
        code: "InsuranceProvider.WebsiteTooLong",
        description: $"El sitio web no puede superar {InsuranceProvider.MaxWebsiteLength} caracteres."
    );
}
