namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Horario libre en el que cabe una cita completa.</summary>
/// <param name="StartsAt">Inicio del horario, en UTC.</param>
/// <param name="EndsAt">Fin del horario, en UTC.</param>
public sealed record AvailableSlotResponse(DateTimeOffset StartsAt, DateTimeOffset EndsAt);
