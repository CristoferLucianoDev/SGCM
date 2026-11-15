using SGCM.Data.Domain.People.InsuranceProviders;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="InsuranceProvider"/>: crea proveedores válidos con datos fijos,
/// para que las pruebas solo declaren lo que les importa.
/// </summary>
public static class InsuranceProviderMother
{
    /// <summary>Nombre de los proveedores creados.</summary>
    public const string DefaultName = "Seguros Universal";

    /// <summary>Teléfono de contacto de los proveedores creados.</summary>
    public const string DefaultContactPhone = "8095550200";

    /// <summary>Correo de contacto de los proveedores creados.</summary>
    public const string DefaultContactEmail = "contacto@seguros.com";

    /// <summary>Dirección de los proveedores creados.</summary>
    public const string DefaultAddress = "Av. Winston Churchill 100, Santo Domingo";

    /// <summary>Sitio web de los proveedores creados.</summary>
    public const string DefaultWebsite = "https://www.segurosuniversal.com";

    /// <summary>
    /// Proveedor activo con todos los datos.
    /// </summary>
    /// <param name="name">Nombre del proveedor; si es <c>null</c>, <see cref="DefaultName"/>.</param>
    /// <param name="website">Sitio web; por defecto <see cref="DefaultWebsite"/>, y <c>null</c> para un proveedor sin sitio.</param>
    public static InsuranceProvider Valid(string? name = null, string? website = DefaultWebsite) =>
        InsuranceProvider.Create(
            name ?? DefaultName,
            DefaultContactPhone,
            DefaultContactEmail,
            DefaultAddress,
            website).Value;
}
