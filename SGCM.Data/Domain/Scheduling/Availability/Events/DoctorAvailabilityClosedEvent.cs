using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Availability.Events;

/// <summary>
/// El médico cerró un bloque: su rango deja de ofrecer horarios. Lleva el rango y el motivo para que las citas
/// activas que quedan dentro se cancelen con ese motivo.
/// </summary>
/// <param name="BlockId">Id del bloque cerrado.</param>
/// <param name="DoctorId">Médico dueño del bloque.</param>
/// <param name="Range">Rango [inicio, fin) en UTC que deja de estar disponible.</param>
/// <param name="Reason">Motivo que escribió el médico; <c>null</c> si no escribió ninguno.</param>
public sealed record DoctorAvailabilityClosedEvent(int BlockId, int DoctorId, TimeRange Range, string? Reason) : IDomainEvent;
