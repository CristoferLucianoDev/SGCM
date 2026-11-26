namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Acciones que se pueden ofrecer sobre una cita. El orden de los miembros es el orden en que se informan.
/// </summary>
public enum AppointmentAction
{
    /// <summary>Cancelar la cita.</summary>
    Cancel,

    /// <summary>Reprogramar la cita a otro horario.</summary>
    Reschedule,

    /// <summary>Cambiar el médico de la cita.</summary>
    ChangeDoctor,

    /// <summary>Confirmar una cita pendiente.</summary>
    Confirm,

    /// <summary>Rechazar una cita pendiente.</summary>
    Reject,

    /// <summary>Extender una cita en curso.</summary>
    Extend,

    /// <summary>Completar una cita en curso.</summary>
    Complete
}
