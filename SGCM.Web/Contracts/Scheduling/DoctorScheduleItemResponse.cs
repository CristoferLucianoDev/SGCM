namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Una cita de la agenda, con la fecha y las horas locales de la agenda.</summary>
/// <param name="Status">Estado en minúsculas: <c>pending</c> o <c>confirmed</c>.</param>
public sealed record DoctorScheduleItemResponse(
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
    string Status,
    bool IsInProgress);
