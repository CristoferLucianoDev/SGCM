namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Horarios libres de la consulta.</summary>
/// <param name="Items">Horarios libres ordenados de menor a mayor; una lista vacía significa que no hay horarios.</param>
public sealed record AvailableSlotsResponse(IReadOnlyList<AvailableSlotResponse> Items);
