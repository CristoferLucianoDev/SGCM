namespace SGCM.Web.Contracts.Scheduling;

/// <summary>
/// Una cita del historial del paciente.
/// </summary>
/// <param name="AppointmentId">Id de la cita.</param>
/// <param name="StartsAt">Inicio de la cita en UTC.</param>
/// <param name="EndsAt">Fin de la cita en UTC.</param>
/// <param name="AppointmentDate">Fecha de la cita en la zona local de la agenda.</param>
/// <param name="StartTime">Hora de inicio en la zona local de la agenda.</param>
/// <param name="EndTime">Hora de fin en la zona local de la agenda.</param>
/// <param name="DoctorId">Id del médico de la cita.</param>
/// <param name="DoctorName">Nombre y apellido del médico.</param>
/// <param name="SpecialtyId">Id de la especialidad de la cita.</param>
/// <param name="SpecialtyName">Nombre de la especialidad.</param>
/// <param name="Status">Estado de la cita: pending, confirmed, rejected, cancelled o completed.</param>
public sealed record PatientAppointmentResponse(
    int AppointmentId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly AppointmentDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int DoctorId,
    string DoctorName,
    int SpecialtyId,
    string SpecialtyName,
    string Status);
