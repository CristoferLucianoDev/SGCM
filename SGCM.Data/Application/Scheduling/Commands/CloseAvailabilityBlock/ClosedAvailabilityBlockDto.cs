namespace SGCM.Data.Application.Scheduling.Commands.CloseAvailabilityBlock;

/// <summary>Resultado de cerrar el bloque.</summary>
/// <param name="CancelledAppointments">Cuántas citas activas se cancelaron con el cierre.</param>
public sealed record ClosedAvailabilityBlockDto(int CancelledAppointments);
