using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Notifications;

/// <summary>
/// Aviso a la otra parte de la cita reprogramada, con la nueva fecha y el horario anterior. Lo usan la
/// reprogramación del paciente y la del médico.
/// </summary>
/// <param name="Recipient">
/// La parte que no reprogramó: con la reprogramación del paciente, el médico de la cita; con la del médico, el paciente.
/// </param>
/// <param name="Appointment">Datos de la cita con su nuevo horario, en la hora local de la agenda.</param>
/// <param name="RescheduledBy">Quién reprogramó la cita.</param>
/// <param name="PreviousDate">Fecha local que tenía la cita antes de reprogramarla.</param>
/// <param name="PreviousStartTime">Hora local de inicio que tenía la cita antes de reprogramarla.</param>
public sealed record AppointmentRescheduledNotification(
    Recipient Recipient,
    AppointmentNotificationData Appointment,
    AppointmentParticipant RescheduledBy,
    DateOnly PreviousDate,
    TimeOnly PreviousStartTime) : Notification(Recipient)
{
    /// <summary>Clave de la plantilla <c>appointment-rescheduled-by-patient.html</c>, la que recibe el médico.</summary>
    public const string RescheduledByPatientTemplateKey = "appointment-rescheduled-by-patient";

    /// <summary>Clave de la plantilla <c>appointment-rescheduled-by-doctor.html</c>, la que recibe el paciente.</summary>
    public const string RescheduledByDoctorTemplateKey = "appointment-rescheduled-by-doctor";

    /// <summary>Clave de la plantilla según quién reprogramó.</summary>
    /// <exception cref="InvalidOperationException">Si <see cref="RescheduledBy"/> no tiene plantilla.</exception>
    public override string TemplateKey => RescheduledBy switch
    {
        AppointmentParticipant.Patient => RescheduledByPatientTemplateKey,
        AppointmentParticipant.Doctor => RescheduledByDoctorTemplateKey,
        _ => throw new InvalidOperationException($"No hay plantilla de reprogramación para {RescheduledBy}.")
    };

    /// <summary>
    /// Crea la notificación para la otra parte de la cita: con la reprogramación del paciente, su médico; con la
    /// del médico, el paciente.
    /// </summary>
    /// <param name="appointment">Datos de la cita ya con su nuevo horario.</param>
    /// <param name="rescheduledBy">Quién reprogramó.</param>
    /// <param name="previousDate">Fecha local anterior de la cita.</param>
    /// <param name="previousStartTime">Hora local de inicio anterior de la cita.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="appointment"/> es nulo.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="rescheduledBy"/> no está contemplado.</exception>
    public static AppointmentRescheduledNotification Create(
        AppointmentNotificationData appointment,
        AppointmentParticipant rescheduledBy,
        DateOnly previousDate,
        TimeOnly previousStartTime)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        var recipient = rescheduledBy switch
        {
            AppointmentParticipant.Patient => new Recipient(appointment.DoctorEmail, appointment.DoctorName),
            AppointmentParticipant.Doctor => new Recipient(appointment.PatientEmail, appointment.PatientName),
            _ => throw new ArgumentOutOfRangeException(nameof(rescheduledBy))
        };

        return new AppointmentRescheduledNotification(recipient, appointment, rescheduledBy, previousDate, previousStartTime);
    }

    /// <summary>Valores que reemplazan las variables de la plantilla; las fechas van como dd/MM/yyyy y las horas en 24 horas.</summary>
    public override IReadOnlyDictionary<string, string> GetValues() =>
        new Dictionary<string, string>
        {
            ["PatientName"] = Appointment.PatientName,
            ["DoctorName"] = Appointment.DoctorName,
            ["SpecialtyName"] = Appointment.SpecialtyName,
            ["AppointmentDate"] = AppointmentNotificationFormats.Date(Appointment.AppointmentDate),
            ["StartTime"] = AppointmentNotificationFormats.Time(Appointment.StartTime),
            ["EndTime"] = AppointmentNotificationFormats.Time(Appointment.EndTime),
            ["PreviousDate"] = AppointmentNotificationFormats.Date(PreviousDate),
            ["PreviousStartTime"] = AppointmentNotificationFormats.Time(PreviousStartTime)
        };
}
