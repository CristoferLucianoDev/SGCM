namespace SGCM.Data.Application.Scheduling.Commands.UpdateAvailabilitySeries;

/// <summary>Resultado de editar la serie.</summary>
/// <param name="EffectiveFrom">Fecha local desde la que aplica la recurrencia nueva.</param>
/// <param name="CancelledAppointments">Cuántas citas activas que quedaron fuera del horario nuevo se cancelaron.</param>
public sealed record UpdatedAvailabilitySeriesDto(DateOnly EffectiveFrom, int CancelledAppointments);
