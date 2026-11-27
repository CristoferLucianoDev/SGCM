namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Reprogramación de una cita propia del médico autenticado.</summary>
/// <param name="StartsAt">Nueva hora de inicio con su desfase, por ejemplo "2027-01-20T09:00:00-04:00"; es obligatoria.</param>
public sealed record RescheduleAppointmentByDoctorRequest(DateTimeOffset? StartsAt = null);
