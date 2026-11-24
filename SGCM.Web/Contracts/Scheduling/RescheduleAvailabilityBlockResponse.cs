namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Resultado de reprogramar el bloque.</summary>
/// <param name="CancelledAppointments">Cuántas citas activas que quedaron fuera del horario nuevo se cancelaron.</param>
public sealed record RescheduleAvailabilityBlockResponse(int CancelledAppointments);
