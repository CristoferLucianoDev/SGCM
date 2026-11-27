namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Un bloque del médico, abierto o cerrado.</summary>
/// <param name="Id">Identificador del bloque.</param>
/// <param name="SeriesId">Serie que lo generó; <c>null</c> si es un bloque suelto.</param>
/// <param name="StartsAt">Inicio, en UTC.</param>
/// <param name="EndsAt">Fin, en UTC.</param>
/// <param name="IsAvailable"><c>false</c> si el médico lo cerró.</param>
/// <param name="IsModified"><c>true</c> si es una ocurrencia editada a mano.</param>
/// <param name="CanReschedule"><c>true</c> si hoy todavía se puede reprogramar; si no, solo se puede cerrar.</param>
public sealed record AvailabilityBlockResponse(
    int Id,
    int? SeriesId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsAvailable,
    bool IsModified,
    bool CanReschedule);
