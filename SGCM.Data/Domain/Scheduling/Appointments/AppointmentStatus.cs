namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Estado de una cita. Las activas son <see cref="Pending"/> y <see cref="Confirmed"/>: solo ellas ocupan el
/// horario del médico y del paciente.
/// </summary>
public enum AppointmentStatus
{
    /// <summary>Solicitada por el paciente; el médico todavía no la confirma.</summary>
    Pending,

    /// <summary>Aceptada por el médico.</summary>
    Confirmed,

    /// <summary>Rechazada por el médico, con su motivo.</summary>
    Rejected,

    /// <summary>Cancelada por el paciente, el médico o el sistema.</summary>
    Cancelled,

    /// <summary>Atendida.</summary>
    Completed
}
