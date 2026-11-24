using System.Globalization;
using SGCM.Data.Application.Abstractions.Notifications;

namespace SGCM.Data.Application.Scheduling.Notifications;

/// <summary>
/// Correo al médico con la nueva solicitud de cita. El paciente no recibe correo hasta que la cita cambie de estado.
/// </summary>
/// <param name="Recipient">El médico de la cita.</param>
/// <param name="Appointment">Datos de la cita, con la hora local de la agenda.</param>
/// <param name="ConfirmationDeadlineHours">Horas antes del inicio hasta las que el médico puede confirmarla.</param>
public sealed record AppointmentRequestedNotification(
    Recipient Recipient,
    AppointmentNotificationData Appointment,
    int ConfirmationDeadlineHours) : Notification(Recipient)
{
    /// <summary>Texto del motivo cuando el paciente no escribió ninguno.</summary>
    public const string NoReason = "El paciente no indicó un motivo.";

    /// <summary>Clave de la plantilla <c>appointment-requested.html</c>.</summary>
    public override string TemplateKey => "appointment-requested";

    /// <summary>Valores que reemplazan las variables de la plantilla; la fecha va como dd/MM/yyyy y las horas en 24 horas.</summary>
    public override IReadOnlyDictionary<string, string> GetValues() =>
        new Dictionary<string, string>
        {
            ["DoctorName"] = Appointment.DoctorName,
            ["PatientName"] = Appointment.PatientName,
            ["SpecialtyName"] = Appointment.SpecialtyName,
            ["AppointmentDate"] = AppointmentNotificationFormats.Date(Appointment.AppointmentDate),
            ["StartTime"] = AppointmentNotificationFormats.Time(Appointment.StartTime),
            ["EndTime"] = AppointmentNotificationFormats.Time(Appointment.EndTime),
            ["ReasonForVisit"] = Appointment.ReasonForVisit ?? NoReason,
            ["ConfirmationDeadlineHours"] = ConfirmationDeadlineHours.ToString(CultureInfo.InvariantCulture)
        };
}
