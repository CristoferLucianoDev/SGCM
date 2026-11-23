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
    /// longitudes se validan sobre esos valores; un sitio web vacío se trata como ausente y,
    /// si viene, debe ser una URL absoluta http o https.
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

        if (normalizedWebsite is not null && !IsHttpUrl(normalizedWebsite))
        {
            return InsuranceProviderErrors.WebsiteInvalid;
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

    /// <summary>
    /// Actualiza solo los campos enviados con las mismas reglas que <see cref="Create"/>: recorte, correo
    /// normalizado, longitudes y sitio web http o https. Un campo obligatorio enviado vacío se rechaza, y un
    /// sitio web enviado nulo o vacío se quita. Es atómico: si algo es inválido no cambia ningún campo. Funciona
    /// igual con el proveedor inactivo, no cambia su estado y no emite eventos. La unicidad del nombre no se
    /// comprueba aquí porque depende de los demás proveedores.
    /// </summary>
    /// <param name="changes">Cambios parciales; los campos no enviados conservan su valor.</param>
    /// <returns>
    /// Éxito, <see cref="InsuranceProviderErrors.NoFieldsProvided"/> si no se envió ningún campo, o el primer
    /// error de validación en el orden nombre, teléfono, correo, dirección y sitio web.
    /// </returns>
    public Result Update(InsuranceProviderChanges changes)
    {
        if (!changes.HasChanges)
        {
            return InsuranceProviderErrors.NoFieldsProvided;
        }

        // Se valida con un candidato armado por Create para reutilizar exactamente las reglas y el orden
        // de validación del registro, sin tocar este agregado hasta que todo sea válido.
        var candidate = Create(
            Pick(changes.Name, Name),
            Pick(changes.ContactPhone, ContactPhone),
            Pick(changes.ContactEmail, ContactEmail.Value),
            Pick(changes.Address, Address),
            Pick(changes.Website, Website));
        if (candidate.IsFailure)
        {
            return candidate.Error;
        }

        ApplyProvided(changes, candidate.Value);
        return Result.Success();
    }

    private static string? Pick(Change<string?> change, string? current) =>
        change.IsProvided ? change.Value : current;

    // Se asignan los valores normalizados del candidato y solo de los campos enviados.
    private void ApplyProvided(InsuranceProviderChanges changes, InsuranceProvider candidate)
    {
        if (changes.Name.IsProvided)
        {
            Name = candidate.Name;
        }

        if (changes.ContactPhone.IsProvided)
        {
            ContactPhone = candidate.ContactPhone;
        }

        if (changes.ContactEmail.IsProvided)
        {
            ContactEmail = candidate.ContactEmail;
        }

        if (changes.Address.IsProvided)
        {
            Address = candidate.Address;
        }

        if (changes.Website.IsProvided)
        {
            Website = candidate.Website;
        }
    }

    // Solo http y https: otros esquemas, como javascript: o data:, serían XSS almacenado
    // cuando una vista use el sitio como enlace.
    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

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
