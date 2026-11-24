using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;
using SGCM.Data.Application.Scheduling.Queries.GetAffectedAppointments;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;

namespace SGCM.Web.Api.Controllers.Scheduling;

[Route("api/doctors/me/availability-blocks")]
public sealed class AvailabilityBlocksController : ApiControllerBase
{
    /// <summary>Crea un bloque suelto, sin serie, para el médico autenticado.</summary>
    /// <remarks>
    /// El médico sale de la sesión, así que solo crea bloques propios. Se rechaza si empieza en el pasado,
    /// si una hora no está en punto, si no cumple la duración mínima o si se traslapa con otro de sus bloques.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(CreateAvailabilityBlockResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateAvailabilityBlockRequest request,
        [FromServices] ICommandHandler<CreateAvailabilityBlockCommand, int> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, new CreateAvailabilityBlockResponse(result.Value))
            : ErrorResult(result.Error);
    }

    /// <summary>Avisa cuántas citas se cancelarían al cerrar o reprogramar un bloque propio, antes de confirmar.</summary>
    /// <remarks>
    /// Sin <c>startsAt</c> ni <c>endsAt</c> evalúa el cierre del bloque; con los dos, la reprogramación a ese horario.
    /// Indica si el motivo es obligatorio y, si alguna cita empieza pronto, la advertencia con las palabras mínimas.
    /// Un bloque de otro médico responde 404.
    /// </remarks>
    [HttpGet("{blockId:int}/affected-appointments")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(AffectedAppointmentsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAffectedAppointments(
        [FromRoute] int blockId,
        [FromQuery] DateTimeOffset? startsAt,
        [FromQuery] DateTimeOffset? endsAt,
        [FromServices] IQueryHandler<GetAffectedAppointmentsQuery, AffectedAppointmentsDto> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetAffectedAppointmentsQuery(blockId, startsAt, endsAt), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }
}
