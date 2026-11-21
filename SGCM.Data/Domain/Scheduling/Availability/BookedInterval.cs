namespace SGCM.Data.Domain.Scheduling.Availability;

/// <summary>
/// Intervalo de una cita activa (pendiente o confirmada) de un médico. Todavía no existe el agregado
/// Appointment: quien consulte construye estos intervalos desde las citas activas.
/// </summary>
/// <param name="AppointmentId">Identificador de la cita, usado para excluirla del cálculo al reprogramarla.</param>
/// <param name="Range">Rango [inicio, fin) que la cita ocupa en UTC; su fin puede no estar en hora en punto.</param>
public sealed record BookedInterval(int AppointmentId, TimeRange Range);
