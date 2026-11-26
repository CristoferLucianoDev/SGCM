using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Appointments.Events;

/// <summary>
/// Se rechazó una cita. Se despacha después de guardar para enviar al paciente el correo con el motivo
/// (<see cref="Appointment.StatusReason"/>).
/// </summary>
/// <param name="Appointment">La cita ya rechazada.</param>
public sealed record AppointmentRejectedEvent(Appointment Appointment) : IDomainEvent;
