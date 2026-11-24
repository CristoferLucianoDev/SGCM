namespace SGCM.Data.Application.Scheduling.Commands.RescheduleAvailabilityBlock;

/// <summary>Resultado de reprogramar el bloque.</summary>
/// <param name="CancelledAppointments">Cuántas citas activas que quedaron fuera del horario nuevo se cancelaron.</param>
public sealed record RescheduledAvailabilityBlockDto(int CancelledAppointments);
