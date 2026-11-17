using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;
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
}
