namespace SGCM.Web.Contracts.Scheduling;

/// <summary>
/// Estado por el que se filtra el historial de citas. Se envía por nombre, sin distinguir mayúsculas, por ejemplo
/// <c>?status=cancelled</c>.
/// </summary>
public enum AppointmentStatusFilter
{
    /// <summary>Citas pendientes de confirmación.</summary>
    Pending = 0,

    /// <summary>Citas confirmadas.</summary>
    Confirmed = 1,

    /// <summary>Citas rechazadas por el médico.</summary>
    Rejected = 2,

    /// <summary>Citas canceladas.</summary>
    Cancelled = 3,

    /// <summary>Citas completadas.</summary>
    Completed = 4
}
