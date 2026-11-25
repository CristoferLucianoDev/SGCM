namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Bloques del médico en el rango pedido, en orden cronológico.</summary>
public sealed record AvailabilityBlocksResponse(IReadOnlyList<AvailabilityBlockResponse> Items);
