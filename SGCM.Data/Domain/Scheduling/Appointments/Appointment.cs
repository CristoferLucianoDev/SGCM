using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments.Events;

namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Cita de un paciente con un médico para una de sus especialidades. Que no se traslape con otra cita activa
/// del médico ni del paciente, y que quepa en un bloque abierto del médico, lo garantiza la base.
/// </summary>
public sealed class Appointment : AggregateRoot
{
    /// <summary>Longitud máxima del motivo de la consulta, igual que en la base.</summary>
    public const int ReasonForVisitMaxLength = 500;

    public int PatientId { get; private set; }

    public int DoctorId { get; private set; }

    public int SpecialtyId { get; private set; }

    /// <summary>Inicio en UTC, en punto.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>Fin en UTC.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    public AppointmentStatus Status { get; private set; }

    /// <summary>Motivo de la consulta que escribió el paciente; <c>null</c> si no escribió ninguno.</summary>
    public string? ReasonForVisit { get; private set; }

    /// <summary>Motivo de la cancelación o del rechazo; <c>null</c> mientras la cita está activa.</summary>
    public string? StatusReason { get; private set; }

    private Appointment()
    {
    }

    /// <summary>
    /// Crea la solicitud de cita del paciente: pendiente, en punto, con la duración de la configuración y con
    /// un inicio dentro de la ventana de reservas. Emite <see cref="AppointmentRequestedEvent"/>.
    /// </summary>
    /// <param name="reasonForVisit">Motivo opcional; se recorta y, si queda vacío, se guarda <c>null</c>.</param>
    /// <param name="durationHours">Duración de la cita en horas, según la configuración.</param>
    /// <param name="bookingWindow">
    /// Ventana en la que puede empezar la cita: su inicio es la antelación mínima y su fin, excluido, el horizonte.
    /// </param>
    /// <returns>La cita con las fechas en UTC, o el primer error de validación encontrado.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración no es positiva.</exception>
    /// <exception cref="ArgumentNullException">Si la ventana es nula.</exception>
    public static Result<Appointment> Request(
        int patientId,
        int doctorId,
        int specialtyId,
        DateTimeOffset startsAt,
        string? reasonForVisit,
        int durationHours,
        TimeRange bookingWindow)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationHours);
        ArgumentNullException.ThrowIfNull(bookingWindow);

        if (patientId <= 0) return AppointmentErrors.InvalidPatient;
        if (doctorId <= 0) return AppointmentErrors.InvalidDoctor;
        if (specialtyId <= 0) return AppointmentErrors.InvalidSpecialty;
        var start = ValidateStart(startsAt, bookingWindow);
        if (start != Error.None) return start;

        var reason = string.IsNullOrWhiteSpace(reasonForVisit) ? null : reasonForVisit.Trim();
        if (reason?.Length > ReasonForVisitMaxLength) return AppointmentErrors.ReasonTooLong;

        var startUtc = startsAt.ToUniversalTime();
        var appointment = new Appointment
        {
            PatientId = patientId,
            DoctorId = doctorId,
            SpecialtyId = specialtyId,
            StartsAt = startUtc,
            EndsAt = startUtc.AddHours(durationHours),
            Status = AppointmentStatus.Pending,
            ReasonForVisit = reason
        };
        appointment.Raise(new AppointmentRequestedEvent(appointment));

        return appointment;
    }

    /// <summary>
    /// Comprueba que la cita pueda empezar en ese instante: en punto y dentro de la ventana de reservas. Permite
    /// rechazar el horario antes de buscar o bloquear al médico, con el mismo error que <see cref="Request"/>.
    /// </summary>
    /// <returns>
    /// <see cref="Error.None"/>, <see cref="AppointmentErrors.NotOnHour"/>, <see cref="AppointmentErrors.TooSoon"/>
    /// o <see cref="AppointmentErrors.BeyondHorizon"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Si la ventana es nula.</exception>
    public static Error ValidateStart(DateTimeOffset startsAt, TimeRange bookingWindow)
    {
        ArgumentNullException.ThrowIfNull(bookingWindow);

        if (!HourAlignedInstant.IsOnHour(startsAt)) return AppointmentErrors.NotOnHour;
        if (startsAt < bookingWindow.Start) return AppointmentErrors.TooSoon;
        if (startsAt >= bookingWindow.End) return AppointmentErrors.BeyondHorizon;

        return Error.None;
    }
}
