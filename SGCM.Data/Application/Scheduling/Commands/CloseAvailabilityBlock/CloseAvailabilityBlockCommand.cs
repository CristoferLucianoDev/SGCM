using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Commands.CloseAvailabilityBlock;

/// <summary>
/// El médico autenticado cierra uno de sus bloques en cualquier semana, incluida la actual.
/// </summary>
/// <param name="BlockId">Bloque que se cierra.</param>
/// <param name="Reason">
/// Motivo; obligatorio si el bloque tiene citas activas, que se cancelan con él, y extendido si alguna empieza pronto.
/// </param>
public sealed record CloseAvailabilityBlockCommand(int BlockId, string? Reason = null) : ICommand<ClosedAvailabilityBlockDto>;
