using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Appointments.Events;

/// <summary>
/// Se terminó una cita. No envía correo; se despacha después de guardar para habilitar los recordatorios de
/// seguimiento.
/// </summary>
/// <param name="Appointment">La cita ya completada, con su hora real de fin.</param>
public sealed record AppointmentCompletedEvent(Appointment Appointment) : IDomainEvent;
