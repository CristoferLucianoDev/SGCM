using System.Text.Json.Serialization;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.InsuranceProviders;

namespace SGCM.Web.Contracts.People;

/// <summary>
/// Datos para actualizar parcialmente un proveedor de seguros. Omitir un campo conserva su valor; enviar el
/// sitio web como <c>null</c> o vacío lo quita. Cada propiedad recuerda si se envió, para distinguir un campo
/// omitido de uno enviado como <c>null</c>.
/// </summary>
public sealed class UpdateInsuranceProviderRequest
{
    private Change<string?> _name;
    private Change<string?> _contactPhone;
    private Change<string?> _contactEmail;
    private Change<string?> _address;
    private Change<string?> _website;

    /// <summary>Nombre nuevo; omitirlo lo conserva y no puede quedar vacío.</summary>
    public string? Name
    {
        get => _name.Value;
        init => _name = Change<string?>.Set(value);
    }

    /// <summary>Teléfono de contacto nuevo; omitirlo lo conserva y no puede quedar vacío.</summary>
    public string? ContactPhone
    {
        get => _contactPhone.Value;
        init => _contactPhone = Change<string?>.Set(value);
    }

    /// <summary>Correo de contacto nuevo; omitirlo lo conserva y no puede quedar vacío.</summary>
    public string? ContactEmail
    {
        get => _contactEmail.Value;
        init => _contactEmail = Change<string?>.Set(value);
    }

    /// <summary>Dirección nueva; omitirla la conserva y no puede quedar vacía.</summary>
    public string? Address
    {
        get => _address.Value;
        init => _address = Change<string?>.Set(value);
    }

    /// <summary>Sitio web nuevo; omitirlo lo conserva y enviarlo <c>null</c> o vacío lo quita.</summary>
    public string? Website
    {
        get => _website.Value;
        init => _website = Change<string?>.Set(value);
    }

    /// <summary>Campos enviados. No se lee ni se escribe desde el JSON.</summary>
    [JsonIgnore]
    public InsuranceProviderChanges Changes => new(_name, _contactPhone, _contactEmail, _address, _website);
}
