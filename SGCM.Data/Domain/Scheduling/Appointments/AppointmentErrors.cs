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

    /// <summary>El motivo de la cancelación supera la longitud máxima.</summary>
    public static readonly Error StatusReasonTooLong = Error.Validation(
        "Appointment.StatusReasonTooLong",
        $"El motivo no puede superar los {Appointment.StatusReasonMaxLength} caracteres.");
}
