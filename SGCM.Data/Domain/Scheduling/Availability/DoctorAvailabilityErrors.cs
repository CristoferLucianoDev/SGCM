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
}
