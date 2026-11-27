namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Detalle de una cita de la agenda con el motivo de consulta y el contacto del paciente.</summary>
/// <param name="Status">Estado en minúsculas: <c>pending</c> o <c>confirmed</c>.</param>
public sealed record DoctorAppointmentDetailResponse(
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
    string Status,
    bool IsInProgress,
    string? ReasonForVisit);
