using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Messaging.ReminderTemplates;

public static class ReminderTemplateErrors
{
    /// <summary>La plantilla no existe, está inactiva o es privada de otro médico; los tres casos responden igual.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "ReminderTemplate.NotFound", "La plantilla de recordatorio no existe.");

    /// <summary>Solo un médico gestiona plantillas, y solo las suyas.</summary>
    public static readonly Error DoctorOnly = Error.Forbidden(
        "ReminderTemplate.DoctorOnly", "Solo un médico puede gestionar plantillas de recordatorio.");

    public static readonly Error InvalidId = Error.Validation(
        "ReminderTemplate.InvalidId", "El identificador de la plantilla debe ser mayor que cero.");

    /// <summary>Las plantillas globales las ven todos los médicos, pero nadie las edita ni las desactiva.</summary>
    public static readonly Error GlobalReadOnly = Error.Forbidden(
        "ReminderTemplate.GlobalReadOnly", "Las plantillas globales no se pueden editar ni desactivar.");

    public static readonly Error NameRequired = Error.Validation(
        "ReminderTemplate.NameRequired", "El nombre de la plantilla es obligatorio.");

    public static readonly Error NameTooLong = Error.Validation(
        "ReminderTemplate.NameTooLong",
        $"El nombre de la plantilla no puede superar {ReminderTemplate.NameMaxLength} caracteres.");

    public static readonly Error SubjectRequired = Error.Validation(
        "ReminderTemplate.SubjectRequired", "El asunto de la plantilla es obligatorio.");

    public static readonly Error SubjectTooLong = Error.Validation(
        "ReminderTemplate.SubjectTooLong",
        $"El asunto de la plantilla no puede superar {ReminderTemplate.SubjectMaxLength} caracteres.");

    public static readonly Error BodyRequired = Error.Validation(
        "ReminderTemplate.BodyRequired", "El cuerpo de la plantilla es obligatorio.");

    public static readonly Error BodyTooLong = Error.Validation(
        "ReminderTemplate.BodyTooLong",
        $"El cuerpo de la plantilla no puede superar {ReminderTemplate.BodyTemplateMaxLength} caracteres.");

    /// <summary>Una variable fuera de la lista permitida; el mensaje la nombra y lista las permitidas.</summary>
    public static Error UnknownVariable(string variable) => Error.Validation(
        "ReminderTemplate.UnknownVariable",
        $"La variable {{{variable}}} no está permitida. Use solo "
        + string.Join(", ", ReminderTemplate.AllowedVariables.Select(name => $"{{{name}}}")) + ".");
}
