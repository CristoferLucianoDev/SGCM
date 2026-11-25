using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;
using SGCM.Data.Application.Scheduling.Commands.UpdateAvailabilitySeries;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;

namespace SGCM.Web.Api.Controllers.Scheduling;

[Route("api/doctors/me/availability-series")]
public sealed class AvailabilitySeriesController : ApiControllerBase
{
    /// <summary>Crea el horario semanal del médico autenticado.</summary>
    /// <remarks>
    /// El médico sale de la sesión. Se rechaza si la fecha desde es pasada, si una hora no está en punto,
    /// si no cumple la duración mínima o si se traslapa con un bloque o con otra serie activa del médico.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(CreateAvailabilitySeriesResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateAvailabilitySeriesRequest request,
        [FromServices] ICommandHandler<CreateAvailabilitySeriesCommand, int> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, new CreateAvailabilitySeriesResponse(result.Value))
            : ErrorResult(result.Error);
    }

    /// <summary>Cambia la recurrencia de una serie propia.</summary>
    /// <remarks>
    /// El cambio aplica desde la primera fecha que la regla de semanas deja reprogramar: el lunes siguiente si hoy es
    /// de lunes a miércoles, o el de la semana después en otro caso; la semana actual no cambia. Los bloques futuros de
    /// la serie que no se editaron a mano ni se cerraron se ajustan al horario nuevo y se generan los de los días
    /// nuevos. Si quedan citas activas fuera del horario nuevo, el motivo es obligatorio y extendido si alguna empieza
    /// en menos de 6 horas; esas citas se cancelan y cada paciente recibe un correo. Una serie de otro médico responde
    /// 404 y un horario que se traslapa con otro del médico, 409.
    /// </remarks>
    [HttpPut("{seriesId:int}")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(UpdateAvailabilitySeriesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromRoute] int seriesId,
        UpdateAvailabilitySeriesRequest request,
        [FromServices] ICommandHandler<UpdateAvailabilitySeriesCommand, UpdatedAvailabilitySeriesDto> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(seriesId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }
}
