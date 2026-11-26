namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Quién consulta una cita; de él depende qué acciones tiene disponibles.
/// </summary>
public enum AppointmentParticipant
{
    /// <summary>El paciente dueño de la cita.</summary>
    Patient = 1,

    /// <summary>El médico de la cita.</summary>
    Doctor = 2
}
