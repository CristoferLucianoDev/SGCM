using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Queries.GetAffectedAppointments;

/// <summary>
/// Avisa al médico, antes de confirmar, cuántas citas se cancelarán al cerrar un bloque propio o, si envía el
/// horario nuevo, al reprogramarlo, y si el motivo es obligatorio o extendido.
/// </summary>
/// <param name="BlockId">Bloque que se cerrará o reprogramará.</param>
/// <param name="StartsAt">Inicio nuevo al reprogramar; sin inicio ni fin se evalúa el cierre.</param>
/// <param name="EndsAt">Fin nuevo al reprogramar; va junto con <paramref name="StartsAt"/>.</param>
public sealed record GetAffectedAppointmentsQuery(
    int BlockId,
    DateTimeOffset? StartsAt = null,
    DateTimeOffset? EndsAt = null) : IQuery<AffectedAppointmentsDto>;
