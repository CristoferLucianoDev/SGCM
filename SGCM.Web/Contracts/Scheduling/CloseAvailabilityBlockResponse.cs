namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Resultado de cerrar el bloque.</summary>
/// <param name="CancelledAppointments">Cuántas citas activas se cancelaron con el cierre.</param>
public sealed record CloseAvailabilityBlockResponse(int CancelledAppointments);
