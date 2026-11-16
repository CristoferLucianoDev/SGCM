using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling.Availability;

/// <summary>
/// Errores de validación de la serie semanal de disponibilidad del médico.
/// </summary>
public static class DoctorAvailabilitySeriesErrors
{
    public static readonly Error InvalidDoctor = Error.Validation(
        "AvailabilitySeries.InvalidDoctor", "El médico de la serie no es válido.");

    public static readonly Error DaysRequired = Error.Validation(
        "AvailabilitySeries.DaysRequired", "Debe elegir al menos un día de la semana.");

    public static readonly Error InvalidDayOfWeek = Error.Validation(
        "AvailabilitySeries.InvalidDayOfWeek", "Uno de los días de la semana no es válido.");

    public static readonly Error TimeNotOnHour = Error.Validation(
        "AvailabilitySeries.TimeNotOnHour", "La hora de inicio y la de fin deben ser en punto.");

    public static readonly Error InvalidTimeRange = Error.Validation(
        "AvailabilitySeries.InvalidTimeRange", "La hora de fin debe ser posterior a la de inicio.");

    public static readonly Error DurationTooShort = Error.Validation(
        "AvailabilitySeries.DurationTooShort", "El horario no cumple la duración mínima de un bloque.");

    public static readonly Error InvalidDateRange = Error.Validation(
        "AvailabilitySeries.InvalidDateRange", "La fecha hasta no puede ser anterior a la fecha desde.");

    public static readonly Error InvalidTimeZone = Error.Validation(
        "AvailabilitySeries.InvalidTimeZone", "La zona horaria de la serie no es válida.");
}
