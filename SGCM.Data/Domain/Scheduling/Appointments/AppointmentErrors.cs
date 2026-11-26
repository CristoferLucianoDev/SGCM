using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Errores de validación de una cita.
/// </summary>
public static class AppointmentErrors
{
    public static readonly Error InvalidPatient = Error.Validation(
        "Appointment.InvalidPatient", "El paciente de la cita no es válido.");

    public static readonly Error InvalidDoctor = Error.Validation(
        "Appointment.InvalidDoctor", "El médico de la cita no es válido.");

    public static readonly Error InvalidSpecialty = Error.Validation(
        "Appointment.InvalidSpecialty", "La especialidad de la cita no es válida.");

    public static readonly Error NotOnHour = Error.Validation(
        "Appointment.NotOnHour", "La cita debe empezar en punto.");

    /// <summary>El horario está antes de la antelación mínima; esas citas se coordinan por teléfono.</summary>
    public static readonly Error TooSoon = Error.Validation(
        "Appointment.TooSoon",
        "El horario no cumple la antelación mínima para reservar en línea; para una cita tan cercana debe llamar al médico.");

    /// <summary>El horario está después del horizonte de reservas.</summary>
    public static readonly Error BeyondHorizon = Error.Validation(
        "Appointment.BeyondHorizon", "El horario está fuera del período en que se pueden reservar citas.");

    public static readonly Error ReasonTooLong = Error.Validation(
        "Appointment.ReasonTooLong",
        $"El motivo de la consulta no puede superar los {Appointment.ReasonForVisitMaxLength} caracteres.");

    /// <summary>El médico ya tiene otra cita activa que se cruza con ese horario.</summary>
    public static readonly Error SlotUnavailable = Error.Conflict(
        "Appointment.SlotUnavailable", "El horario no está disponible.");

    /// <summary>El paciente ya tiene otra cita activa que se cruza con ese horario.</summary>
    public static readonly Error PatientOverlap = Error.Conflict(
        "Appointment.PatientOverlap", "El horario se traslapa con otra cita tuya.");

    /// <summary>Solo un paciente agenda citas.</summary>
    public static readonly Error PatientOnly = Error.Forbidden(
        "Appointment.PatientOnly", "Solo los pacientes pueden agendar citas.");

    /// <summary>El médico que eligió el paciente no atiende la especialidad, o no está activo.</summary>
    public static readonly Error DoctorNotInSpecialty = Error.Validation(
        "Appointment.DoctorNotInSpecialty", "El médico elegido no atiende esa especialidad.");

    /// <summary>La cita no está pendiente ni confirmada, así que ya no se puede cancelar.</summary>
    public static readonly Error InvalidTransition = Error.Conflict(
        "Appointment.InvalidTransition", "Solo se puede cancelar una cita pendiente o confirmada.");

    /// <summary>El paciente intentó cancelar después del plazo; esas citas se cancelan por teléfono.</summary>
    public static readonly Error CancellationDeadlinePassed = Error.Conflict(
        "Appointment.CancellationDeadlinePassed",
        "El plazo para cancelar en línea ya pasó; para cancelar esta cita debe llamar al médico.");

    /// <summary>La cita no está pendiente, así que el médico ya no puede confirmarla ni rechazarla.</summary>
    public static readonly Error NotPending = Error.Conflict(
        "Appointment.NotPending", "Solo se puede confirmar o rechazar una cita pendiente.");

    /// <summary>El médico intentó confirmar o rechazar después del plazo.</summary>
    public static readonly Error ConfirmationDeadlinePassed = Error.Conflict(
        "Appointment.ConfirmationDeadlinePassed", "El plazo para confirmar o rechazar esta cita ya pasó.");

    /// <summary>El motivo de la cancelación supera la longitud máxima.</summary>
    public static readonly Error StatusReasonTooLong = Error.Validation(
        "Appointment.StatusReasonTooLong",
        $"El motivo no puede superar los {Appointment.StatusReasonMaxLength} caracteres.");

    /// <summary>Quien cancela como médico no indicó el motivo, que es obligatorio.</summary>
    public static readonly Error StatusReasonRequired = Error.Validation(
        "Appointment.StatusReasonRequired", "Debe indicar el motivo.");

    /// <summary>El médico cancela con poco aviso y su motivo no llega al mínimo de palabras.</summary>
    /// <param name="minimumWords">Palabras mínimas exigidas, según la configuración.</param>
    public static Error ShortNoticeReasonTooShort(int minimumWords) => Error.Validation(
        "Appointment.ShortNoticeReasonTooShort",
        $"Faltan pocas horas para la cita: el motivo debe tener al menos {minimumWords} palabras.");

    /// <summary>
    /// La cita no existe o no es del paciente; en ambos casos se responde igual para no revelar citas de otros
    /// pacientes.
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        "Appointment.NotFound", "La cita no existe.");

    /// <summary>Solo un paciente consulta su historial de citas.</summary>
    public static readonly Error HistoryPatientOnly = Error.Forbidden(
        "Appointment.HistoryPatientOnly", "Solo los pacientes pueden consultar su historial de citas.");

    /// <summary>Solo el médico de la cita la cancela desde el caso de uso del médico.</summary>
    public static readonly Error CancellationDoctorOnly = Error.Forbidden(
        "Appointment.CancellationDoctorOnly", "Solo el médico de la cita puede cancelarla desde aquí.");

    /// <summary>Solo el paciente dueño de la cita la cancela desde este caso de uso.</summary>
    public static readonly Error CancellationPatientOnly = Error.Forbidden(
        "Appointment.CancellationPatientOnly", "Solo el paciente puede cancelar sus citas desde aquí.");
}
