namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Resultado de editar la serie.</summary>
/// <param name="EffectiveFrom">Fecha local desde la que aplica la recurrencia nueva.</param>
/// <param name="CancelledAppointments">Cuántas citas activas que quedaron fuera del horario nuevo se cancelaron.</param>
public sealed record UpdateAvailabilitySeriesResponse(DateOnly EffectiveFrom, int CancelledAppointments);
