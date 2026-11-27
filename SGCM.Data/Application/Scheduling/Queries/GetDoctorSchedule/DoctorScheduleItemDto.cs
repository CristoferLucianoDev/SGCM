using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;

/// <summary>Una cita de la agenda del médico, con la fecha y las horas locales de la agenda.</summary>
public sealed record DoctorScheduleItemDto(
    int AppointmentId,
    int PatientId,
    string PatientName,
    int SpecialtyId,
    string SpecialtyName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly AppointmentDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    AppointmentStatus Status,
    bool IsInProgress);
