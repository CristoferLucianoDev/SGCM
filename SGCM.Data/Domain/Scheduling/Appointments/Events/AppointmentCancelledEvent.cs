using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Appointments.Events;

/// <summary>
/// Se canceló una cita. Se despacha después de guardar para notificar a la otra parte con el motivo
/// (<see cref="Appointment.StatusReason"/>) y anular los recordatorios de la cita.
/// </summary>
/// <param name="Appointment">La cita ya cancelada.</param>
/// <param name="CancelledBy">Quién la canceló.</param>
public sealed record AppointmentCancelledEvent(Appointment Appointment, AppointmentCanceller CancelledBy) : IDomainEvent;
