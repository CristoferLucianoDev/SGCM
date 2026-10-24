using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;

namespace SGCM.Data.Domain.People.InsuranceProviders;

/// <summary>
/// Proveedor de seguros médicos con el que trabaja el centro. Admite baja lógica: no se
/// borra porque los pacientes pueden seguir referenciándolo.
/// </summary>
public sealed class InsuranceProvider : ActivatableAggregateRoot
{
    /// <summary>Longitud máxima del nombre, en caracteres.</summary>
    public const int MaxNameLength = 150;

    /// <summary>Longitud máxima del teléfono de contacto, en caracteres.</summary>
    public const int MaxContactPhoneLength = 20;

    /// <summary>Longitud máxima de la dirección, en caracteres.</summary>
    public const int MaxAddressLength = 250;

    /// <summary>Longitud máxima del sitio web, en caracteres.</summary>
    public const int MaxWebsiteLength = 250;

    /// <summary>Nombre del proveedor. Es único en el sistema.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Teléfono de contacto.</summary>
    public string ContactPhone { get; private set; } = null!;

    /// <summary>Correo de contacto.</summary>
    public Email ContactEmail { get; private set; } = null!;

    /// <summary>Dirección física.</summary>
    public string Address { get; private set; } = null!;

    /// <summary>Sitio web, o <c>null</c> si el proveedor no tiene uno.</summary>
    public string? Website { get; private set; }

    /// <inheritdoc />
    protected override string EntityName => nameof(InsuranceProvider);

    private InsuranceProvider()
    {
    }

    /// <summary>
    /// Crea un proveedor activo. Los textos se guardan sin espacios en los extremos y las
    /// longitudes se validan sobre esos valores; un sitio web vacío se trata como ausente.
    /// La unicidad del nombre no se comprueba aquí porque depende de los demás proveedores.
    /// </summary>
    /// <param name="name">Nombre del proveedor.</param>
    /// <param name="contactPhone">Teléfono de contacto.</param>
    /// <param name="contactEmail">Correo de contacto.</param>
    /// <param name="address">Dirección física.</param>
    /// <param name="website">Sitio web opcional.</param>
    /// <returns>El proveedor, o el primer error de validación encontrado, en el orden de los parámetros.</returns>
    public static Result<InsuranceProvider> Create(
        string? name,
        string? contactPhone,
        string? contactEmail,
        string? address,
        string? website)
    {
        var nameResult = RequiredText(name, MaxNameLength, InsuranceProviderErrors.NameRequired, InsuranceProviderErrors.NameTooLong);
        if (nameResult.IsFailure)
        {
            return nameResult.Error;
        }

        var phoneResult = RequiredText(contactPhone, MaxContactPhoneLength,
            InsuranceProviderErrors.ContactPhoneRequired, InsuranceProviderErrors.ContactPhoneTooLong);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var emailResult = Email.Create(contactEmail);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var addressResult = RequiredText(address, MaxAddressLength,
            InsuranceProviderErrors.AddressRequired, InsuranceProviderErrors.AddressTooLong);
        if (addressResult.IsFailure)
        {
            return addressResult.Error;
        }

        var normalizedWebsite = string.IsNullOrWhiteSpace(website) ? null : website.Trim();
        if (normalizedWebsite?.Length > MaxWebsiteLength)
        {
            return InsuranceProviderErrors.WebsiteTooLong;
        }

        return new InsuranceProvider
        {
            Name = nameResult.Value,
            ContactPhone = phoneResult.Value,
            ContactEmail = emailResult.Value,
            Address = addressResult.Value,
            Website = normalizedWebsite
        };
    }

    private static Result<string> RequiredText(string? value, int maxLength, Error required, Error tooLong)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return required;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? tooLong : trimmed;
    }
}
