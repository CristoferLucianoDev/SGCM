namespace SGCM.Data.Domain.Scheduling.Availability;

/// <summary>
/// Un día en que se repite una serie, con su inicio y fin ya convertidos a UTC.
/// </summary>
/// <param name="Date">Fecha local, en la zona horaria de la serie.</param>
/// <param name="StartsAt">Inicio del bloque en UTC.</param>
/// <param name="EndsAt">Fin del bloque en UTC.</param>
public sealed record AvailabilityOccurrence(DateOnly Date, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
