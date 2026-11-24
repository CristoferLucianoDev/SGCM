using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;

/// <summary>
/// Una cita del historial del paciente, con el médico y la especialidad ya resueltos.
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
/// <param name="Status">Estado actual de la cita.</param>
public sealed record PatientAppointmentDto(
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
    AppointmentStatus Status);
