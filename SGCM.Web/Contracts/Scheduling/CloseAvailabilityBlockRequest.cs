namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Cierre de un bloque propio.</summary>
/// <param name="Reason">
/// Motivo; obligatorio si el bloque tiene citas activas, que se cancelan con ese motivo, y de al menos las palabras
/// mínimas si alguna empieza pronto.
/// </param>
public sealed record CloseAvailabilityBlockRequest(string? Reason = null);
