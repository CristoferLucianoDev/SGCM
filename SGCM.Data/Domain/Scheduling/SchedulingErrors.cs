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
}
