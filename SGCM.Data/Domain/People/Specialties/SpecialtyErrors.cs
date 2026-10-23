using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.People.Specialties;

/// <summary>
/// Errores de validación del agregado <see cref="Specialty"/>.
/// </summary>
public static class SpecialtyErrors
{
    public static readonly Error NameRequired = Error.Validation(
        code: "Specialty.NameRequired",
        description: "El nombre de la especialidad es obligatorio."
    );

    public static readonly Error NameTooLong = Error.Validation(
        code: "Specialty.NameTooLong",
        description: $"El nombre de la especialidad no puede superar {Specialty.MaxNameLength} caracteres."
    );

    public static readonly Error DescriptionRequired = Error.Validation(
        code: "Specialty.DescriptionRequired",
        description: "La descripción de la especialidad es obligatoria."
    );

    public static readonly Error DescriptionTooLong = Error.Validation(
        code: "Specialty.DescriptionTooLong",
        description: $"La descripción de la especialidad no puede superar {Specialty.MaxDescriptionLength} caracteres."
    );
}
