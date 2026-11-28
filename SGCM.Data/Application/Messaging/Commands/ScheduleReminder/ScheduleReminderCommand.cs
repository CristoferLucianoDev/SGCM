using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Messaging.Commands.ScheduleReminder;

/// <summary>
/// El médico autenticado programa un recordatorio para una cita propia confirmada o completada. El texto es el final:
/// el de la vista previa, editado o no, o un mensaje libre.
/// </summary>
/// <param name="AppointmentId">Cita del recordatorio.</param>
/// <param name="TemplateId">Plantilla de la que salió el texto, como referencia; <c>null</c> si es un mensaje libre.</param>
/// <param name="Message">Texto final que recibe el paciente, hasta 2000 caracteres.</param>
/// <param name="ScheduledAt">Hora de envío con su desfase; es obligatoria, debe ser futura y la valida el validador.</param>
public sealed record ScheduleReminderCommand(int AppointmentId, int? TemplateId, string? Message, DateTimeOffset? ScheduledAt)
    : ICommand<ScheduledReminderDto>;
