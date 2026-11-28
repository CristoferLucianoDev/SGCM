using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Messaging.Commands.ScheduleReminder;
using SGCM.Data.Application.Messaging.Queries.GetReminderPreview;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Messaging;
using SGCM.Web.Mappers.Messaging;

namespace SGCM.Web.Api.Controllers.Messaging;

[Route("api/doctors/me/appointments")]
public sealed class DoctorRemindersController : ApiControllerBase
{
    /// <summary>Completa una plantilla de recordatorio con los datos reales de una cita del médico autenticado.</summary>
    /// <remarks>
    /// Devuelve el asunto y el texto con las variables reemplazadas para que el médico los edite antes de programar el
    /// recordatorio; no guarda nada. Solo citas propias confirmadas o completadas y plantillas activas, globales o del
    /// médico. 400 con code responde Appointment.InvalidAppointmentId o ReminderTemplate.InvalidId si un id no es
    /// mayor que cero (también si falta templateId). 404 responde Appointment.NotFound si la cita es ajena o no existe,
    /// y ReminderTemplate.NotFound si la plantilla no existe, está inactiva o es de otro médico. 409 responde
    /// Reminder.AppointmentNotEligible si la cita está pendiente, rechazada o cancelada.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="templateId">Id de la plantilla, mayor que cero.</param>
    /// <param name="handler">Manejador de la consulta.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpGet("{id:int}/reminders/preview")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(ReminderPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Preview(
        int id,
        [FromQuery] int templateId,
        [FromServices] IQueryHandler<GetReminderPreviewQuery, ReminderPreviewDto> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetReminderPreviewQuery(id, templateId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }

    /// <summary>Programa un recordatorio para una cita del médico autenticado.</summary>
    /// <remarks>
    /// Guarda el texto final (el de la vista previa, editado o no, o un mensaje libre) y la plantilla solo como
    /// referencia: cambiar la plantilla después no altera el texto. A su hora el job de envío lo manda al paciente y
    /// registra el resultado. Solo citas propias confirmadas o completadas, a una fecha y hora futuras. 400 con code
    /// responde Reminder.MessageRequired, Reminder.MessageTooLong, Reminder.ScheduledAtRequired,
    /// Reminder.ScheduledInPast, Appointment.InvalidAppointmentId o ReminderTemplate.InvalidId. 404 responde
    /// Appointment.NotFound si la cita es ajena o no existe, y ReminderTemplate.NotFound si la plantilla no existe,
    /// está inactiva o es de otro médico. 409 responde Reminder.AppointmentNotEligible si la cita está pendiente,
    /// rechazada o cancelada. Un cuerpo mal formado responde 400 sin code.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="request">Texto, fecha y hora, y plantilla opcional.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost("{id:int}/reminders")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(ScheduledReminderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Schedule(
        int id,
        ScheduleReminderRequest request,
        [FromServices] ICommandHandler<ScheduleReminderCommand, ScheduledReminderDto> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value.ToResponse())
            : ErrorResult(result.Error);
    }
}
