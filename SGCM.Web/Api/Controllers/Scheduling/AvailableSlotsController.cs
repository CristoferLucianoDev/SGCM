using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;

namespace SGCM.Web.Api.Controllers.Scheduling;

[Route("api/doctors")]
public sealed class AvailableSlotsController : ApiControllerBase
{
    /// <summary>Lista los horarios libres de un médico para el paciente autenticado.</summary>
    /// <remarks>
    /// La ventana va desde la antelación mínima de reserva hasta el horizonte de reservas. Los horarios van en
    /// UTC, un bloque cerrado no ofrece horarios y el resultado no se pagina porque lo acota el horizonte.
    /// </remarks>
    [HttpGet("{doctorId:int}/available-slots")]
    [Authorize(Roles = nameof(UserRole.Patient))]
    [ProducesResponseType(typeof(AvailableSlotsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListByDoctor(
        [FromRoute] int doctorId,
        [FromServices] IQueryHandler<GetAvailableSlotsQuery, IReadOnlyList<AvailableSlotDto>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetAvailableSlotsQuery(doctorId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }

    /// <summary>Lista los horarios libres propios del médico autenticado.</summary>
    /// <remarks>
    /// El médico sale de la sesión. La ventana va desde el instante actual hasta el horizonte de reservas. Los
    /// horarios van en UTC, un bloque cerrado no ofrece horarios y el resultado no se pagina porque lo acota el
    /// horizonte.
    /// </remarks>
    [HttpGet("me/available-slots")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(AvailableSlotsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListOwn(
        [FromServices] IQueryHandler<GetAvailableSlotsQuery, IReadOnlyList<AvailableSlotDto>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetAvailableSlotsQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }
}
