using System.Globalization;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.People.Specialties;

/// <summary>
/// Especialidad médica del catálogo, que admite baja lógica.
/// </summary>
public sealed class Specialty : ActivatableAggregateRoot
{
    public const int MaxNameLength = 100;

    public const int MaxDescriptionLength = 500;

    public string Name { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    protected override string EntityName => nameof(Specialty);

    private Specialty()
    {
    }

    public Result Update(bool nameProvided, string? name, bool descriptionProvided, string? description)
    {
        if (!nameProvided && !descriptionProvided)
        {
            return SpecialtyErrors.NoFieldsProvided;
        }

        var candidate = Create(nameProvided ? name : Name, descriptionProvided ? description : Description);
        if (candidate.IsFailure)
        {
            return candidate.Error;
        }

        if (nameProvided)
        {
            Name = candidate.Value.Name;
        }

        if (descriptionProvided)
        {
            Description = candidate.Value.Description;
        }

        return Result.Success();
    }

    /// <summary>
    /// Crea una especialidad activa y valida las longitudes sobre los valores normalizados.
    /// El formato título usa cultura invariable para que el nombre no dependa del entorno.
    /// </summary>
    /// <returns>La especialidad, o el primer error de validación encontrado.</returns>
    public static Result<Specialty> Create(string? name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SpecialtyErrors.NameRequired;
        }

        // Pasar a minúsculas evita que ToTitleCase conserve palabras completas en mayúsculas.
        var normalizedName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Trim().ToLowerInvariant());
        if (normalizedName.Length > MaxNameLength)
        {
            return SpecialtyErrors.NameTooLong;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return SpecialtyErrors.DescriptionRequired;
        }

        var trimmedDescription = description.Trim();
        if (trimmedDescription.Length > MaxDescriptionLength)
        {
            return SpecialtyErrors.DescriptionTooLong;
        }

        return new Specialty
        {
            Name = normalizedName,
            Description = trimmedDescription
        };
    }
}
