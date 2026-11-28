using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Domain.Messaging.Reminders;

/// <summary>
/// Recordatorio de una cita para el paciente. Guarda el texto final ya renderizado, así que cambiar la plantilla
/// después no lo altera; la plantilla queda solo como referencia. Solo un recordatorio pendiente puede enviarse,
/// anularse o cambiar de hora.
/// </summary>
public sealed class Reminder : AggregateRoot
{
    /// <summary>Longitud máxima del mensaje, igual a la columna message_body.</summary>
    public const int MessageMaxLength = 2000;

    private Reminder()
    {
    }

    public int AppointmentId { get; private set; }

    /// <summary>Plantilla de la que salió el texto; <c>null</c> si es un mensaje libre.</summary>
    public int? TemplateId { get; private set; }

    /// <summary>Texto final que recibe el paciente.</summary>
    public string MessageBody { get; private set; } = null!;

    /// <summary>Hora de envío, en UTC.</summary>
    public DateTimeOffset ScheduledAt { get; private set; }

    /// <summary>Hora en que se envió, en UTC; <c>null</c> mientras no se haya enviado.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    public ReminderStatus Status { get; private set; }

    /// <summary>Médico que lo programó; <c>null</c> en el recordatorio automático.</summary>
    public int? CreatedBy { get; private set; }

    /// <summary>
    /// Indica si una cita en ese estado admite recordatorios programados por el médico: solo confirmadas o completadas.
    /// </summary>
    /// <returns>Éxito, o <see cref="ReminderErrors.AppointmentNotEligible"/>.</returns>
    public static Result EnsureAppointmentEligible(AppointmentStatus status) =>
        status is AppointmentStatus.Confirmed or AppointmentStatus.Completed
            ? Result.Success()
            : ReminderErrors.AppointmentNotEligible;

    /// <summary>
    /// Programa un recordatorio pendiente con el texto ya renderizado y la hora de envío.
    /// </summary>
    /// <param name="messageBody">Texto final; se recorta.</param>
    /// <param name="scheduledAt">Hora de envío; debe ser posterior a <paramref name="now"/>.</param>
    /// <param name="createdBy">Usuario del médico que lo programa; <c>null</c> en el automático.</param>
    /// <returns>El recordatorio, o el primer error de validación.</returns>
    /// <exception cref="DomainException">Si la cita o la plantilla no son identificadores válidos.</exception>
    public static Result<Reminder> Schedule(
        int appointmentId,
        int? templateId,
        string? messageBody,
        DateTimeOffset scheduledAt,
        DateTimeOffset now,
        int? createdBy = null)
    {
        // Los identificadores llegan del caso de uso, no del usuario: uno inválido es un error de programación.
        if (appointmentId <= 0) throw new DomainException("La cita del recordatorio no es válida.");
        if (templateId <= 0) throw new DomainException("La plantilla del recordatorio no es válida.");
        if (createdBy <= 0) throw new DomainException("El usuario que programa el recordatorio no es válido.");

        var message = messageBody?.Trim();
        if (string.IsNullOrEmpty(message)) return ReminderErrors.MessageRequired;
        if (message.Length > MessageMaxLength) return ReminderErrors.MessageTooLong;
        if (scheduledAt <= now) return ReminderErrors.ScheduledInPast;

        return new Reminder
        {
            AppointmentId = appointmentId,
            TemplateId = templateId,
            MessageBody = message,
            ScheduledAt = scheduledAt.ToUniversalTime(),
            Status = ReminderStatus.Pending,
            CreatedBy = createdBy
        };
    }

    /// <summary>Indica si ya llegó la hora de enviarlo.</summary>
    public bool IsDue(DateTimeOffset now) => Status == ReminderStatus.Pending && ScheduledAt <= now;

    /// <summary>Registra el envío exitoso.</summary>
    public Result MarkSent(DateTimeOffset now)
    {
        if (Status != ReminderStatus.Pending) return ReminderErrors.NotPending;

        Status = ReminderStatus.Sent;
        SentAt = now.ToUniversalTime();
        return Result.Success();
    }

    /// <summary>Registra que el envío falló; no se reintenta.</summary>
    public Result MarkFailed()
    {
        if (Status != ReminderStatus.Pending) return ReminderErrors.NotPending;

        Status = ReminderStatus.Failed;
        return Result.Success();
    }

    /// <summary>Anula el recordatorio antes de enviarse.</summary>
    public Result Cancel()
    {
        if (Status != ReminderStatus.Pending) return ReminderErrors.NotPending;

        Status = ReminderStatus.Cancelled;
        return Result.Success();
    }

    /// <summary>Cambia la hora de envío; la nueva hora debe ser futura.</summary>
    public Result Reschedule(DateTimeOffset scheduledAt, DateTimeOffset now)
    {
        if (Status != ReminderStatus.Pending) return ReminderErrors.NotPending;
        if (scheduledAt <= now) return ReminderErrors.ScheduledInPast;

        ScheduledAt = scheduledAt.ToUniversalTime();
        return Result.Success();
    }
}
