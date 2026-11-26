using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Appointments.Events;

/// <summary>
/// Se confirmó una cita. Se despacha después de guardar para enviar al paciente el correo con los datos de la cita
/// y programar su recordatorio.
/// </summary>
/// <param name="Appointment">La cita ya confirmada.</param>
public sealed record AppointmentConfirmedEvent(Appointment Appointment) : IDomainEvent;
