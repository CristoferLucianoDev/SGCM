using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Appointments.Events;

/// <summary>
/// El paciente solicitó una cita. Se despacha después de guardar, cuando la cita ya tiene su Id, para
/// notificar al médico; el paciente no recibe correo hasta que la cita cambie de estado.
/// </summary>
/// <param name="Appointment">La cita solicitada, pendiente.</param>
public sealed record AppointmentRequestedEvent(Appointment Appointment) : IDomainEvent;
