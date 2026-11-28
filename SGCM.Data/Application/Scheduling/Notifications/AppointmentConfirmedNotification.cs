using System.Globalization;
using SGCM.Data.Application.Abstractions.Notifications;

namespace SGCM.Data.Application.Scheduling.Notifications;

/// <summary>
/// Correo al paciente con los datos de su cita confirmada por el médico. El médico no recibe correo.
/// </summary>
/// <param name="Recipient">El paciente de la cita.</param>
/// <param name="Appointment">Datos de la cita, con la hora local de la agenda.</param>
public sealed record AppointmentConfirmedNotification(
    Recipient Recipient,
    AppointmentNotificationData Appointment) : Notification(Recipient)
{
    /// <summary>Clave de la plantilla <c>appointment-confirmed.html</c>.</summary>
    public const string ConfirmedTemplateKey = "appointment-confirmed";

    /// <summary>Formato de la tarifa: separador de miles y dos decimales, por ejemplo 1,500.00.</summary>
    public const string ConsultationFeeFormat = "N2";

    /// <summary>Texto que se muestra si no se conoce la dirección de consulta del médico.</summary>
    public const string NoAddress = "Consulte la dirección en el perfil del médico.";

    /// <summary>Clave de la plantilla de la notificación.</summary>
    public override string TemplateKey => ConfirmedTemplateKey;

    /// <summary>
    /// Valores que reemplazan las variables de la plantilla; la fecha va como dd/MM/yyyy, las horas en 24 horas y la
    /// tarifa con dos decimales. La dirección es la de consulta del médico.
    /// </summary>
    public override IReadOnlyDictionary<string, string> GetValues() =>
        new Dictionary<string, string>
        {
            ["PatientName"] = Appointment.PatientName,
            ["DoctorName"] = Appointment.DoctorName,
            ["SpecialtyName"] = Appointment.SpecialtyName,
            ["AppointmentDate"] = AppointmentNotificationFormats.Date(Appointment.AppointmentDate),
            ["StartTime"] = AppointmentNotificationFormats.Time(Appointment.StartTime),
            ["EndTime"] = AppointmentNotificationFormats.Time(Appointment.EndTime),
            ["ConsultationFee"] = Appointment.ConsultationFee.ToString(ConsultationFeeFormat, CultureInfo.InvariantCulture),
            ["DoctorAddress"] = string.IsNullOrWhiteSpace(Appointment.DoctorAddress) ? NoAddress : Appointment.DoctorAddress
        };
}
