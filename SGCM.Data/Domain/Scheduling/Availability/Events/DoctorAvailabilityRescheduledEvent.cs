using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Availability.Events;

/// <summary>
/// Un bloque cambió de horario y dejó de cubrir parte de su horario anterior. Lleva esos rangos y el motivo para
/// que las citas activas que quedan fuera se cancelen con ese motivo.
/// </summary>
/// <param name="BlockId">Id del bloque reprogramado.</param>
/// <param name="DoctorId">Médico dueño del bloque.</param>
/// <param name="RemovedRanges">Rangos [inicio, fin) en UTC del horario anterior que el nuevo ya no cubre.</param>
/// <param name="Reason">Motivo que escribió el médico; <c>null</c> si no escribió ninguno.</param>
public sealed record DoctorAvailabilityRescheduledEvent(
    int BlockId, int DoctorId, IReadOnlyList<TimeRange> RemovedRanges, string? Reason) : IDomainEvent;
