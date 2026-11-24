namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Quién cancela la cita. Cada uno tiene sus reglas en <see cref="Appointment.Cancel"/>.
/// </summary>
public enum AppointmentCanceller
{
    /// <summary>El paciente dueño de la cita; solo puede cancelar dentro del plazo.</summary>
    Patient = 1,

    /// <summary>El médico de la cita; cancela en cualquier momento, con el motivo obligatorio.</summary>
    Doctor = 2
}
