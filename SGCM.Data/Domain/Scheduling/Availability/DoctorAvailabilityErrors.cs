using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling.Availability;

/// <summary>
/// Errores de validación de un bloque de disponibilidad del médico.
/// </summary>
public static class DoctorAvailabilityErrors
{
    public static readonly Error InvalidDoctor = Error.Validation(
        "Availability.InvalidDoctor", "El médico del bloque no es válido.");

    public static readonly Error InvalidSeries = Error.Validation(
        "Availability.InvalidSeries", "La serie del bloque no es válida.");

    public static readonly Error NotOnHour = Error.Validation(
        "Availability.NotOnHour", "El inicio y el fin del bloque deben ser en punto.");

    public static readonly Error InvalidTimeRange = Error.Validation(
        "Availability.InvalidTimeRange", "El fin del bloque debe ser posterior a su inicio.");

    public static readonly Error DurationTooShort = Error.Validation(
        "Availability.DurationTooShort", "El bloque no cumple la duración mínima.");

    /// <summary>Un bloque que ya empezó no se puede cambiar.</summary>
    public static readonly Error AlreadyStarted = Error.Validation(
        "Availability.AlreadyStarted", "El bloque ya empezó y no se puede cambiar.");

    /// <summary>Solo un bloque generado por una serie se edita como ocurrencia.</summary>
    public static readonly Error NotAnOccurrence = Error.Validation(
        "Availability.NotAnOccurrence", "Solo un bloque de una serie puede editarse como ocurrencia.");

    /// <summary>Editar una ocurrencia cambia el horario de ese día, no la mueve a otro.</summary>
    public static readonly Error OccurrenceDayChanged = Error.Validation(
        "Availability.OccurrenceDayChanged", "La ocurrencia solo puede cambiar de horario dentro de su mismo día.");

    /// <summary>Un bloque suelto solo puede crearse para un horario futuro.</summary>
    public static readonly Error StartsInPast = Error.Validation(
        "Availability.StartsInPast", "El bloque debe empezar en el futuro.");

    /// <summary>El bloque se cruza con otro del mismo médico, abierto o cerrado.</summary>
    public static readonly Error Overlap = Error.Conflict(
        "Availability.Overlap", "El horario se traslapa con otro horario del mismo médico.");

    /// <summary>El bloque ya está cerrado.</summary>
    public static readonly Error AlreadyClosed = Error.Validation(
        "Availability.AlreadyClosed", "El bloque ya está cerrado.");

    /// <summary>Un bloque que ya terminó no se puede cerrar.</summary>
    public static readonly Error AlreadyEnded = Error.Validation(
        "Availability.AlreadyEnded", "El bloque ya terminó y no se puede cerrar.");
}
