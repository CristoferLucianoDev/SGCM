using System.Globalization;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Notifications;

/// <summary>
/// Aviso del vencimiento de una solicitud de cita que el médico no confirmó a tiempo. Hay una plantilla por
/// destinatario: el paciente y el médico reciben textos distintos.
/// </summary>
/// <param name="Recipient">La parte a la que se avisa: el paciente o el médico de la cita.</param>
/// <param name="Appointment">Datos de la cita, con la hora local de la agenda.</param>
/// <param name="RecipientRole">Rol del destinatario, que decide la plantilla.</param>
/// <param name="ConfirmationDeadlineHours">Horas de antelación mínima con que se debía confirmar, según la configuración.</param>
public sealed record AppointmentExpiredNotification(
    Recipient Recipient,
    AppointmentNotificationData Appointment,
    AppointmentParticipant RecipientRole,
    int ConfirmationDeadlineHours) : Notification(Recipient)
{
    /// <summary>Clave de la plantilla <c>appointment-expired-for-patient.html</c>.</summary>
    public const string ForPatientTemplateKey = "appointment-expired-for-patient";

    /// <summary>Clave de la plantilla <c>appointment-expired-for-doctor.html</c>.</summary>
    public const string ForDoctorTemplateKey = "appointment-expired-for-doctor";

    /// <summary>Clave de la plantilla según el destinatario.</summary>
    /// <exception cref="InvalidOperationException">Si <see cref="RecipientRole"/> no tiene plantilla.</exception>
    public override string TemplateKey => RecipientRole switch
    {
        AppointmentParticipant.Patient => ForPatientTemplateKey,
        AppointmentParticipant.Doctor => ForDoctorTemplateKey,
        _ => throw new InvalidOperationException($"No hay plantilla de vencimiento para {RecipientRole}.")
    };

    /// <summary>Crea el aviso para una de las dos partes de la cita vencida.</summary>
    /// <param name="appointment">Datos de la cita vencida.</param>
    /// <param name="recipientRole">A quién se avisa.</param>
    /// <param name="confirmationDeadlineHours">Horas de antelación mínima con que se debía confirmar.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="appointment"/> es nulo.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="recipientRole"/> no está contemplado.</exception>
    public static AppointmentExpiredNotification Create(
        AppointmentNotificationData appointment,
        AppointmentParticipant recipientRole,
        int confirmationDeadlineHours)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        var recipient = recipientRole switch
        {
            AppointmentParticipant.Patient => new Recipient(appointment.PatientEmail, appointment.PatientName),
            AppointmentParticipant.Doctor => new Recipient(appointment.DoctorEmail, appointment.DoctorName),
            _ => throw new ArgumentOutOfRangeException(nameof(recipientRole))
        };

        return new AppointmentExpiredNotification(recipient, appointment, recipientRole, confirmationDeadlineHours);
    }

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
            ["ConfirmationDeadlineHours"] = ConfirmationDeadlineHours.ToString(CultureInfo.InvariantCulture)
        };
}
