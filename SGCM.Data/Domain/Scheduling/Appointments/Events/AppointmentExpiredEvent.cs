using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Appointments.Events;

/// <summary>
/// Venció una cita porque el médico no la confirmó a tiempo. Se despacha después de guardar para avisar al paciente
/// y al médico; los recordatorios pendientes los anula la base al pasar la cita a cancelada.
/// </summary>
/// <param name="Appointment">La cita ya vencida.</param>
public sealed record AppointmentExpiredEvent(Appointment Appointment) : IDomainEvent;
