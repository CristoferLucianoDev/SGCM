using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorAppointmentDetail;

/// <summary>
/// Detalle de una cita de la agenda: el motivo de consulta y los datos de contacto del paciente, con la fecha y las
/// horas locales de la agenda.
/// </summary>
public sealed record DoctorAppointmentDetailDto(
    int AppointmentId,
    int PatientId,
    string PatientName,
    string PatientPhoneNumber,
    string PatientEmail,
    int SpecialtyId,
    string SpecialtyName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly AppointmentDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    AppointmentStatus Status,
    bool IsInProgress,
    string? ReasonForVisit);
