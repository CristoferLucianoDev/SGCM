using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.People.Commands.ActivateSpecialty;
using SGCM.Data.Application.People.Commands.CreateSpecialty;
using SGCM.Data.Application.People.Commands.DeactivateSpecialty;
using SGCM.Data.Application.People.Commands.UpdateSpecialty;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.ListSpecialties;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Common;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.People;
using SGCM.Web.Mappers.People;

namespace SGCM.Web.Api.Controllers.People;

[Route("api/specialties")]
public sealed class SpecialtiesController : ApiControllerBase
{
    [HttpPatch("{id:int}/deactivate")]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(
        [FromRoute] int id,
        [FromServices] ICommandHandler<DeactivateSpecialtyCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeactivateSpecialtyCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }

    [HttpPatch("{id:int}/activate")]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Activate(
        [FromRoute] int id,
        [FromServices] ICommandHandler<ActivateSpecialtyCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ActivateSpecialtyCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }

    [HttpPatch("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromRoute] int id,
        [FromBody] UpdateSpecialtyRequest request,
        [FromServices] ICommandHandler<UpdateSpecialtyCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ListSpecialtiesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] ListSpecialtiesRequest request,
        [FromServices] IQueryHandler<ListSpecialtiesQuery, PagedResult<SpecialtyDto>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    [ProducesResponseType(typeof(CreateSpecialtyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateSpecialtyRequest request,
        [FromServices] ICommandHandler<CreateSpecialtyCommand, int> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, new CreateSpecialtyResponse(result.Value))
            : ErrorResult(result.Error);
    }
}
