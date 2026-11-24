namespace SGCM.Data.Application.Scheduling.Notifications;

/// <summary>
/// Datos que usan todas las notificaciones de una cita, con la fecha y las horas ya en la zona local de la agenda.
/// </summary>
/// <param name="AppointmentId">Id de la cita.</param>
/// <param name="PatientName">Nombre y apellido del paciente.</param>
/// <param name="PatientEmail">Correo de la cuenta del paciente.</param>
/// <param name="DoctorName">Nombre y apellido del médico.</param>
/// <param name="DoctorEmail">Correo de la cuenta del médico.</param>
/// <param name="SpecialtyName">Especialidad de la cita.</param>
/// <param name="ConsultationFee">Tarifa de consulta del médico.</param>
/// <param name="AppointmentDate">Fecha local de inicio.</param>
/// <param name="StartTime">Hora local de inicio.</param>
/// <param name="EndTime">Hora local de fin.</param>
/// <param name="ReasonForVisit">Motivo que escribió el paciente; <c>null</c> si no escribió ninguno.</param>
public sealed record AppointmentNotificationData(
    int AppointmentId,
    string PatientName,
    string PatientEmail,
    string DoctorName,
    string DoctorEmail,
    string SpecialtyName,
    decimal ConsultationFee,
    DateOnly AppointmentDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? ReasonForVisit);
