using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Messaging;

/// <summary>
/// Variables que admite una plantilla de recordatorio y su valor para una cita. Son las mismas que acepta la base
/// (ck_reminder_template_variables), con la fecha y la hora en el formato de los correos de citas.
/// </summary>
public static class AppointmentTemplateValues
{
    public const string PatientName = nameof(PatientName);
    public const string DoctorName = nameof(DoctorName);
    public const string AppointmentDate = nameof(AppointmentDate);
    public const string StartTime = nameof(StartTime);
    public const string SpecialtyName = nameof(SpecialtyName);

    /// <summary>Las variables permitidas en una plantilla de recordatorio.</summary>
    public static IReadOnlyCollection<string> Names { get; } = ReminderTemplate.AllowedVariables;

    /// <summary>El valor de cada variable para la cita.</summary>
    public static IReadOnlyDictionary<string, string> From(AppointmentNotificationData appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        return new Dictionary<string, string>
        {
            [PatientName] = appointment.PatientName,
            [DoctorName] = appointment.DoctorName,
            [AppointmentDate] = AppointmentNotificationFormats.Date(appointment.AppointmentDate),
            [StartTime] = AppointmentNotificationFormats.Time(appointment.StartTime),
            [SpecialtyName] = appointment.SpecialtyName
        };
    }
}
