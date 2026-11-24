using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Notifications;

/// <summary>
/// Correo a la otra parte de la cita cancelada. La plantilla depende de quién canceló; la tarea del cierre de
/// bloque agregará aquí su caso.
/// </summary>
/// <param name="Recipient">
/// La parte que no canceló: con la cancelación del paciente, el médico de la cita; con la del médico, el paciente.
/// </param>
/// <param name="Appointment">Datos de la cita, con la hora local de la agenda.</param>
/// <param name="CancelledBy">Quién canceló la cita.</param>
/// <param name="CancellationReason">Motivo de la cancelación; <c>null</c> si no se indicó.</param>
public sealed record AppointmentCancelledNotification(
    Recipient Recipient,
    AppointmentNotificationData Appointment,
    AppointmentCanceller CancelledBy,
    string? CancellationReason) : Notification(Recipient)
{
    /// <summary>Texto del motivo cuando quien canceló no escribió ninguno.</summary>
    public const string NoReason = "No se indicó un motivo.";

    /// <summary>Clave de la plantilla <c>appointment-cancelled-by-patient.html</c>.</summary>
    public const string CancelledByPatientTemplateKey = "appointment-cancelled-by-patient";

    /// <summary>
    /// Clave de la plantilla <c>appointment-cancelled-for-patient.html</c>, la que recibe el paciente. La usan la
    /// cancelación del médico y, más adelante, el cierre de bloques.
    /// </summary>
    public const string CancelledForPatientTemplateKey = "appointment-cancelled-for-patient";

    /// <summary>Clave de la plantilla según quién canceló.</summary>
    /// <exception cref="InvalidOperationException">Si <see cref="CancelledBy"/> no tiene plantilla.</exception>
    public override string TemplateKey => CancelledBy switch
    {
        AppointmentCanceller.Patient => CancelledByPatientTemplateKey,
        AppointmentCanceller.Doctor => CancelledForPatientTemplateKey,
        _ => throw new InvalidOperationException($"No hay plantilla de cancelación para {CancelledBy}.")
    };

    /// <summary>
    /// Crea la notificación para la otra parte de la cita: con la cancelación del paciente, su médico; con la
    /// del médico, el paciente.
    /// </summary>
    /// <param name="appointment">Datos de la cita cancelada.</param>
    /// <param name="cancelledBy">Quién canceló.</param>
    /// <param name="cancellationReason">Motivo guardado en la cita; <c>null</c> si no hay.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="appointment"/> es nulo.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="cancelledBy"/> no está contemplado.</exception>
    public static AppointmentCancelledNotification Create(
        AppointmentNotificationData appointment,
        AppointmentCanceller cancelledBy,
        string? cancellationReason)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        var recipient = cancelledBy switch
        {
            AppointmentCanceller.Patient => new Recipient(appointment.DoctorEmail, appointment.DoctorName),
            AppointmentCanceller.Doctor => new Recipient(appointment.PatientEmail, appointment.PatientName),
            _ => throw new ArgumentOutOfRangeException(nameof(cancelledBy))
        };

        return new AppointmentCancelledNotification(recipient, appointment, cancelledBy, cancellationReason);
    }

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
            ["CancellationReason"] = CancellationReason ?? NoReason
        };
}
