namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Extensión de una cita propia en curso del médico autenticado.</summary>
/// <param name="AdditionalHours">Horas enteras a sumar al fin actual, entre 1 y 24.</param>
public sealed record ExtendAppointmentRequest(int AdditionalHours);
