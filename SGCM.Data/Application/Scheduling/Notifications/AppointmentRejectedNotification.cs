using SGCM.Data.Application.Abstractions.Notifications;

namespace SGCM.Data.Application.Scheduling.Notifications;

/// <summary>
/// Correo al paciente con el motivo por el que el médico rechazó su solicitud de cita. El médico no recibe correo.
/// </summary>
/// <param name="Recipient">El paciente de la cita.</param>
/// <param name="Appointment">Datos de la cita, con la hora local de la agenda.</param>
/// <param name="RejectionReason">Motivo del rechazo; <c>null</c> si no se indicó.</param>
public sealed record AppointmentRejectedNotification(
    Recipient Recipient,
    AppointmentNotificationData Appointment,
    string? RejectionReason) : Notification(Recipient)
{
    /// <summary>Texto del motivo cuando no hay ninguno; es el mismo que usan las cancelaciones.</summary>
    public const string NoReason = AppointmentCancelledNotification.NoReason;

    /// <summary>Clave de la plantilla <c>appointment-rejected.html</c>.</summary>
    public const string RejectedTemplateKey = "appointment-rejected";

    /// <summary>Clave de la plantilla de la notificación.</summary>
    public override string TemplateKey => RejectedTemplateKey;

    /// <summary>Valores que reemplazan las variables de la plantilla; la fecha va como dd/MM/yyyy y las horas en 24 horas.</summary>
    public override IReadOnlyDictionary<string, string> GetValues() =>
        new Dictionary<string, string>
        {
            ["PatientName"] = Appointment.PatientName,
            ["DoctorName"] = Appointment.DoctorName,
            ["SpecialtyName"] = Appointment.SpecialtyName,
            ["AppointmentDate"] = AppointmentNotificationFormats.Date(Appointment.AppointmentDate),
            ["StartTime"] = AppointmentNotificationFormats.Time(Appointment.StartTime),
            ["EndTime"] = AppointmentNotificationFormats.Time(Appointment.EndTime),
            ["RejectionReason"] = RejectionReason ?? NoReason
        };
}
