using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Messaging.Reminders;

public static class ReminderErrors
{
    public static readonly Error MessageRequired = Error.Validation(
        "Reminder.MessageRequired", "El mensaje del recordatorio es obligatorio.");

    public static readonly Error MessageTooLong = Error.Validation(
        "Reminder.MessageTooLong",
        $"El mensaje del recordatorio no puede superar los {Reminder.MessageMaxLength} caracteres.");

    public static readonly Error ScheduledInPast = Error.Validation(
        "Reminder.ScheduledInPast", "La fecha y hora del recordatorio deben ser futuras.");

    /// <summary>Solo un médico programa recordatorios, y solo para sus citas.</summary>
    public static readonly Error DoctorOnly = Error.Forbidden(
        "Reminder.DoctorOnly", "Solo el médico de la cita puede programar sus recordatorios.");

    public static readonly Error AppointmentNotEligible = Error.Conflict(
        "Reminder.AppointmentNotEligible", "Solo se programan recordatorios para citas confirmadas o completadas.");

    public static readonly Error NotPending = Error.Conflict(
        "Reminder.NotPending", "Solo un recordatorio pendiente puede enviarse, anularse o cambiar de hora.");
}
