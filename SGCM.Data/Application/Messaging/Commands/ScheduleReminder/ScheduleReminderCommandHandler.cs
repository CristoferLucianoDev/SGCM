using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Messaging.Commands.ScheduleReminder;

/// <summary>
/// Programa el recordatorio del médico autenticado. La cita propia y bloqueada la entrega
/// <see cref="DoctorAppointmentAccess"/>, así que no cambia de estado mientras se programa. Se guarda el texto final con
/// la plantilla solo como referencia, y quien lo programó; el envío lo hace SendDueRemindersCommand a su hora.
/// </summary>
internal sealed class ScheduleReminderCommandHandler(
    DoctorAppointmentAccess access,
    IReminderTemplateRepository templates,
    IReminderRepository reminders,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : ICommandHandler<ScheduleReminderCommand, ScheduledReminderDto>
{
    public async Task<Result<ScheduledReminderDto>> HandleAsync(
        ScheduleReminderCommand command, CancellationToken cancellationToken)
    {
        var found = await access.GetOwnForUpdateAsync(command.AppointmentId, ReminderErrors.DoctorOnly, cancellationToken);
        if (found.IsFailure) return found.Error;

        var appointment = found.Value;
        var eligible = Reminder.EnsureAppointmentEligible(appointment.Status);
        if (eligible.IsFailure) return eligible.Error;

        if (command.TemplateId is { } templateId)
        {
            var template = await templates.GetByIdAsync(templateId, cancellationToken);
            if (template is null || !template.IsUsableBy(appointment.DoctorId)) return ReminderTemplateErrors.NotFound;
        }

        var scheduled = Reminder.Schedule(
            appointment.Id, command.TemplateId, command.Message, command.ScheduledAt!.Value, clock.UtcNow,
            createdBy: currentUser.UserId);
        if (scheduled.IsFailure) return scheduled.Error;

        var reminder = scheduled.Value;
        reminders.Add(reminder);
        // El id se genera al guardar; el decorador de transacción confirma.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ScheduledReminderDto(
            reminder.Id, reminder.AppointmentId, reminder.TemplateId, reminder.MessageBody, reminder.ScheduledAt);
    }
}
