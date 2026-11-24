namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Quién cancela la cita. Por ahora solo el paciente; la tarea de cancelar la cita por el médico agrega
/// <c>Doctor</c> con sus reglas en el mismo método <see cref="Appointment.Cancel"/>.
/// </summary>
public enum AppointmentCanceller
{
    /// <summary>El paciente dueño de la cita; solo puede cancelar dentro del plazo.</summary>
    Patient = 1
}
