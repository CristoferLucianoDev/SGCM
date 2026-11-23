using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.People.Queries.ListMunicipalities;
using SGCM.Data.Application.People.Queries.ListProvinces;
using SGCM.Data.Domain.Common;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.People;
using SGCM.Web.Mappers.People;

namespace SGCM.Web.Api.Controllers.People;

[Route("api/locations")]
public sealed class LocationsController(ILogger<LocationsController> logger) : ApiControllerBase
{
    private static readonly Error CatalogReadFailed =
        Error.Failure("Location.CatalogReadFailed", "Ocurrió un error al consultar el catálogo geográfico.");

    [HttpGet("provinces")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProvinceResponse[]), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public Task<IActionResult> ListProvinces(
        [FromServices] IQueryHandler<ListProvincesQuery, IReadOnlyList<ProvinceDto>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(new ListProvincesQuery(), handler, province => province.ToResponse(), cancellationToken);

    [HttpGet("provinces/{provinceId:int}/municipalities")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MunicipalityResponse[]), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public Task<IActionResult> ListMunicipalities(
        [FromRoute] int provinceId,
        [FromServices] IQueryHandler<ListMunicipalitiesQuery, IReadOnlyList<MunicipalityDto>> handler,
        CancellationToken cancellationToken) =>
        ReadAsync(new ListMunicipalitiesQuery(provinceId), handler, municipality => municipality.ToResponse(), cancellationToken);

    private async Task<IActionResult> ReadAsync<TQuery, TItem, TResponse>(
        TQuery query, IQueryHandler<TQuery, IReadOnlyList<TItem>> handler,
        Func<TItem, TResponse> map, CancellationToken cancellationToken)
        where TQuery : IQuery<IReadOnlyList<TItem>>
    {
        try
        {
            var result = await handler.HandleAsync(query, cancellationToken);
            return result.IsSuccess ? Ok(result.Value.Select(map).ToArray()) : ErrorResult(result.Error);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Fallo al consultar el catálogo geográfico ({ExceptionType}). Petición {TraceId}.",
                exception.GetType().Name, HttpContext.TraceIdentifier);
            return ErrorResult(CatalogReadFailed);
        }
    }
}
