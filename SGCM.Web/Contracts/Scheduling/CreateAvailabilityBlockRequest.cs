namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Horario puntual que el médico autenticado agrega fuera de su serie.</summary>
/// <param name="Date">Día del bloque; no puede ser pasado.</param>
/// <param name="StartTime">Hora de inicio en punto, por ejemplo "08:00:00".</param>
/// <param name="EndTime">Hora de fin en punto, por ejemplo "14:00:00".</param>
public sealed record CreateAvailabilityBlockRequest(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);
