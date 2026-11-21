namespace SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;

/// <summary>
/// Horario libre en el que cabe una cita completa.
/// </summary>
/// <param name="StartsAt">Inicio del horario, en UTC.</param>
/// <param name="EndsAt">Fin del horario, en UTC: el inicio más la duración configurada de la cita.</param>
public sealed record AvailableSlotDto(DateTimeOffset StartsAt, DateTimeOffset EndsAt);
