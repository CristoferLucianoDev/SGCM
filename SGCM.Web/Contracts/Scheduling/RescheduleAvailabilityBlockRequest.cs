namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Horario nuevo de un bloque propio, en la fecha y las horas locales de la agenda.</summary>
/// <param name="Date">Fecha local del horario nuevo.</param>
/// <param name="StartTime">Hora local de inicio, en punto.</param>
/// <param name="EndTime">Hora local de fin.</param>
/// <param name="Reason">
/// Motivo; obligatorio si quedan citas activas fuera del horario nuevo, que se cancelan con ese motivo.
/// </param>
public sealed record RescheduleAvailabilityBlockRequest(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string? Reason = null);
