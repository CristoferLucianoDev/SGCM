using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Messaging;

/// <summary>
/// Programa, recalcula y anula el recordatorio automático de una cita. El recordatorio sale
/// <see cref="SchedulingOptions.PatientReminderLeadHours"/> antes del inicio con la plantilla global
/// <see cref="ReminderTemplate.AutomaticReminderName"/>; si para entonces la hora ya pasó, no se programa. Lo usan los
/// manejadores de eventos de la cita, que corren después del commit, así que guarda sus propios cambios. Un error de
/// configuración se lanza como excepción y el despachador de eventos lo registra sin afectar la cita.
/// </summary>
internal sealed class AutomaticReminderScheduler(
    IReminderRepository reminders,
    IReminderTemplateRepository templates,
    IAppointmentNotificationReader appointmentReader,
    ReminderComposer composer,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions)
{
    /// <summary>Programa el recordatorio automático de la cita, si todavía hay tiempo.</summary>
    /// <returns><c>true</c> si quedó programado.</returns>
    public async Task<bool> ScheduleAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        var now = clock.UtcNow;
        var scheduledAt = appointment.StartsAt.AddHours(-schedulingOptions.Value.PatientReminderLeadHours);
        if (scheduledAt <= now) return false;

        var template = await templates.GetActiveGlobalByNameAsync(ReminderTemplate.AutomaticReminderName, cancellationToken);
        // La plantilla es un dato semilla: si falta es un error de configuración, que el despachador de eventos registra.
        if (template is null)
            throw new InvalidOperationException(
                $"No existe la plantilla global activa '{ReminderTemplate.AutomaticReminderName}' del recordatorio automático.");

        // Solo falta si la cita se borró entre el commit y la lectura; no hay a quién recordar.
        var data = await appointmentReader.GetAsync(appointment.Id, cancellationToken);
        if (data is null) return false;

        var composed = composer.Compose(template, data);
        var reminder = Reminder.Schedule(appointment.Id, template.Id, composed.Message, scheduledAt, now);
        // La hora ya se comprobó; solo falla si la plantilla produce un texto vacío o demasiado largo.
        if (reminder.IsFailure)
            throw new InvalidOperationException(
                $"No se pudo programar el recordatorio automático de la cita {appointment.Id}: {reminder.Error.Description}");

        reminders.Add(reminder.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Anula el recordatorio automático pendiente de la cita; los que programó el médico no cambian.</summary>
    public Task<int> CancelAutomaticAsync(int appointmentId, CancellationToken cancellationToken) =>
        CancelAsync(appointmentId, reminder => reminder.CreatedBy is null, cancellationToken);

    /// <summary>Anula todos los recordatorios pendientes de la cita, automáticos o del médico.</summary>
    public Task<int> CancelAllAsync(int appointmentId, CancellationToken cancellationToken) =>
        CancelAsync(appointmentId, _ => true, cancellationToken);

    private async Task<int> CancelAsync(
        int appointmentId, Func<Reminder, bool> applies, CancellationToken cancellationToken)
    {
        var pending = await reminders.ListPendingByAppointmentAsync(appointmentId, cancellationToken);
        var cancelled = pending.Where(applies).Count(reminder => reminder.Cancel().IsSuccess);
        if (cancelled > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return cancelled;
    }
}
