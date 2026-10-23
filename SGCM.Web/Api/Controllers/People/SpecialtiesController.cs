using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.People.Commands.CreateSpecialty;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.People;
using SGCM.Web.Mappers.People;

namespace SGCM.Web.Api.Controllers.People;

[Route("api/specialties")]
public sealed class SpecialtiesController : ApiControllerBase
{
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
