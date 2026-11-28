using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Messaging.Commands.CreateReminderTemplate;
using SGCM.Data.Application.Messaging.Commands.DeactivateReminderTemplate;
using SGCM.Data.Application.Messaging.Commands.UpdateReminderTemplate;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Messaging;
using SGCM.Web.Mappers.Messaging;

namespace SGCM.Web.Api.Controllers.Messaging;

[Route("api/doctors/me/reminder-templates")]
public sealed class DoctorReminderTemplatesController : ApiControllerBase
{
    /// <summary>Crea una plantilla de recordatorio propia del médico autenticado.</summary>
    /// <remarks>
    /// La plantilla queda activa. 400 con code responde ReminderTemplate.NameRequired, NameTooLong, SubjectRequired,
    /// SubjectTooLong, BodyRequired, BodyTooLong o UnknownVariable (el mensaje nombra la variable y lista las
    /// permitidas). Un cuerpo mal formado responde 400 sin code.
    /// </remarks>
    /// <param name="request">Nombre, asunto y cuerpo.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(ReminderTemplateResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        ReminderTemplateRequest request,
        [FromServices] ICommandHandler<CreateReminderTemplateCommand, ReminderTemplateDto> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCreateCommand(), cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value.ToResponse())
            : ErrorResult(result.Error);
    }

    /// <summary>Reemplaza nombre, asunto y cuerpo de una plantilla propia.</summary>
    /// <remarks>
    /// Los recordatorios ya programados o enviados no cambian. 400 con code responde ReminderTemplate.InvalidId o los
    /// mismos errores de datos que al crear. 403 con code ReminderTemplate.GlobalReadOnly si la plantilla es global.
    /// 404 con code ReminderTemplate.NotFound si no existe o es de otro médico.
    /// </remarks>
    /// <param name="id">Id de la plantilla, mayor que cero.</param>
    /// <param name="request">Nombre, asunto y cuerpo nuevos.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(ReminderTemplateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        ReminderTemplateRequest request,
        [FromServices] ICommandHandler<UpdateReminderTemplateCommand, ReminderTemplateDto> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToUpdateCommand(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }

    /// <summary>Desactiva una plantilla propia.</summary>
    /// <remarks>
    /// Deja de ofrecerse; los recordatorios ya programados con ella se envían igual. 400 con code
    /// ReminderTemplate.InvalidId. 403 con code ReminderTemplate.GlobalReadOnly si es global. 404 con code
    /// ReminderTemplate.NotFound si no existe o es de otro médico. 409 con code ReminderTemplate.AlreadyInactive.
    /// </remarks>
    /// <param name="id">Id de la plantilla, mayor que cero.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPatch("{id:int}/deactivate")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(
        int id,
        [FromServices] ICommandHandler<DeactivateReminderTemplateCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeactivateReminderTemplateCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }
}
