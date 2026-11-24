using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling;

/// <summary>
/// Errores de los objetos de valor de agenda. Cada agregado puede traducirlos a su propio código.
/// </summary>
public static class SchedulingErrors
{
    public static readonly Error NotOnHour = Error.Validation(
        "Scheduling.NotOnHour", "El instante debe estar en hora en punto.");

    public static readonly Error InvalidTimeRange = Error.Validation(
        "Scheduling.InvalidTimeRange", "El fin del rango debe ser posterior a su inicio.");

    public static readonly Error InvalidTimeZone = Error.Validation(
        "Scheduling.InvalidTimeZone", "La zona horaria no es válida.");

    /// <summary>Un horario de la semana actual no se reprograma; el bloque solo se puede cerrar.</summary>
    public static readonly Error CurrentWeekLocked = Error.Validation(
        "Scheduling.CurrentWeekLocked", "Un bloque de la semana actual no puede reprogramarse; solo se puede cerrar.");

    /// <summary>Un horario de la semana siguiente solo se reprograma de lunes a miércoles.</summary>
    public static readonly Error NextWeekLocked = Error.Validation(
        "Scheduling.NextWeekLocked", "Los bloques de la semana siguiente solo pueden reprogramarse de lunes a miércoles.");
}
