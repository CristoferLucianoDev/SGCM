using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Appointments.Events;

/// <summary>
/// Se reprogramó una cita. Se despacha después de guardar para avisar a la otra parte con la nueva fecha y
/// reprogramar el recordatorio.
/// </summary>
/// <param name="Appointment">La cita ya reprogramada, con su nuevo horario.</param>
/// <param name="RescheduledBy">Quién la reprogramó; el aviso va a la otra parte.</param>
/// <param name="PreviousStartsAt">Inicio que tenía la cita antes de reprogramarla, en UTC.</param>
public sealed record AppointmentRescheduledEvent(
    Appointment Appointment,
    AppointmentParticipant RescheduledBy,
    DateTimeOffset PreviousStartsAt) : IDomainEvent;
