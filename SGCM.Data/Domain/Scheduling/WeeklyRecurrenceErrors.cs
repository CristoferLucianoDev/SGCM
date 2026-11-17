using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling;

/// <summary>
/// Errores de la recurrencia semanal. La serie de disponibilidad los traduce a sus propios códigos.
/// </summary>
public static class WeeklyRecurrenceErrors
{
    public static readonly Error DaysRequired = Error.Validation(
        "WeeklyRecurrence.DaysRequired", "Debe elegir al menos un día de la semana.");

    public static readonly Error InvalidDayOfWeek = Error.Validation(
        "WeeklyRecurrence.InvalidDayOfWeek", "Uno de los días de la semana no es válido.");

    public static readonly Error TimeNotOnHour = Error.Validation(
        "WeeklyRecurrence.TimeNotOnHour", "La hora de inicio y la de fin deben ser en punto.");

    public static readonly Error InvalidTimeRange = Error.Validation(
        "WeeklyRecurrence.InvalidTimeRange", "La hora de fin debe ser posterior a la de inicio.");

    public static readonly Error DurationTooShort = Error.Validation(
        "WeeklyRecurrence.DurationTooShort", "El horario no cumple la duración mínima.");

    public static readonly Error InvalidDateRange = Error.Validation(
        "WeeklyRecurrence.InvalidDateRange", "La fecha hasta no puede ser anterior a la fecha desde.");
}
