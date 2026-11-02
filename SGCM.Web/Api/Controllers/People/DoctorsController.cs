using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.People.Commands.AssociateDoctorSpecialty;
using SGCM.Data.Application.People.Commands.DisassociateDoctorSpecialty;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;

namespace SGCM.Web.Api.Controllers.People;

[Route("api/doctors/me/specialties")]
public sealed class DoctorsController : ApiControllerBase
{
    [HttpPost("{specialtyId:int}")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Associate(
        [FromRoute] int specialtyId,
        [FromServices] ICommandHandler<AssociateDoctorSpecialtyCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new AssociateDoctorSpecialtyCommand(specialtyId), cancellationToken);
        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }

    [HttpDelete("{specialtyId:int}")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Disassociate(
        [FromRoute] int specialtyId,
        [FromServices] ICommandHandler<DisassociateDoctorSpecialtyCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DisassociateDoctorSpecialtyCommand(specialtyId), cancellationToken);
        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }
}
