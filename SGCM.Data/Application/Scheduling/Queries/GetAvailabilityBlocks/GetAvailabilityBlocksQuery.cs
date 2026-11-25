using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Queries.GetAvailabilityBlocks;

/// <summary>Bloques del médico autenticado entre dos fechas locales de la agenda, ambas incluidas, hasta 31 días.</summary>
/// <param name="From">Primer día, incluido.</param>
/// <param name="To">Último día, incluido.</param>
public sealed record GetAvailabilityBlocksQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<AvailabilityBlockDto>>
{
    /// <summary>Días que abarca la consulta como máximo, contando el primero y el último.</summary>
    public const int MaximumDays = 31;
}
