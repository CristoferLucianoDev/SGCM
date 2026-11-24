using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Solicitudes de cita válidas. La ventana es la de un paciente que reserva el lunes 4 de enero de 2027 con
/// antelación de 4 días y horizonte de 90: desde el viernes 8 de enero hasta el 5 de abril, a las 00:00 de
/// Santo Domingo.
/// </summary>
public static class AppointmentMother
{
    public const int PatientId = 7;
    public const int DoctorId = 3;
    public const int SpecialtyId = 2;
    public const int DurationHours = 2;
    public const string Reason = "Dolor de cabeza frecuente";
    public const int PatientCancellationDeadlineHours = 24;
    public const int ShortNoticeThresholdHours = 6;
    public const int ShortNoticeMinimumWords = 30;

    public static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);

    public static readonly DateTimeOffset WindowStart = new(2027, 1, 8, 0, 0, 0, SantoDomingo);
    public static readonly DateTimeOffset WindowEnd = new(2027, 4, 5, 0, 0, 0, SantoDomingo);
    public static readonly TimeRange BookingWindow = TimeRange.Create(WindowStart, WindowEnd).Value;

    /// <summary>Martes 12 de enero de 2027 a las 9:00 en Santo Domingo.</summary>
    public static readonly DateTimeOffset StartsAt = new(2027, 1, 12, 9, 0, 0, SantoDomingo);

    /// <summary>Regla de la cancelación del médico con poco aviso, con los valores de la configuración de pruebas.</summary>
    public static readonly ShortNoticeCancellation ShortNotice = new(ShortNoticeThresholdHours, ShortNoticeMinimumWords);

    public static Result<Appointment> Request(
        DateTimeOffset? startsAt = null,
        string? reasonForVisit = Reason,
        int patientId = PatientId,
        int doctorId = DoctorId,
        int specialtyId = SpecialtyId) =>
        Appointment.Request(
            patientId, doctorId, specialtyId, startsAt ?? StartsAt, reasonForVisit, DurationHours, BookingWindow);

    public static Appointment Requested(DateTimeOffset? startsAt = null) => Request(startsAt).Value;

    /// <summary>
    /// Cita en el estado indicado y sin eventos pendientes. Usa reflexión porque el agregado todavía
    /// no expone confirmar, rechazar ni completar; cuando existan, debe reemplazarse por ellos.
    /// </summary>
    /// <param name="status">Estado en el que queda la cita.</param>
    public static Appointment InStatus(AppointmentStatus status)
    {
        var appointment = Requested();
        typeof(Appointment).GetProperty(nameof(Appointment.Status))!.SetValue(appointment, status);
        appointment.ClearDomainEvents();
        return appointment;
    }
}
