using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;
using SGCM.Data.Application.People.Queries.ListInsuranceProviders;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.People;
using SGCM.Web.Mappers.People;

namespace SGCM.Web.Api.Controllers.People;

[Route("api/insurance-providers")]
public sealed class InsuranceProvidersController : ApiControllerBase
{
    /// <summary>
    /// Registra un proveedor de seguros activo y devuelve su id.
    /// </summary>
    /// <remarks>
    /// Solo para administradores. El nombre, el teléfono, el correo y la dirección son
    /// obligatorios; el sitio web es opcional y, si se envía, debe ser una URL absoluta http o
    /// https (InsuranceProvider.WebsiteInvalid).
    /// 400 indica en "code" y en el título el campo inválido. Un correo ausente o mal formado
    /// responde con el código Common.InvalidEmail.
    /// 409 aparece si ya existe un proveedor con ese nombre, activo o inactivo.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    [ProducesResponseType(typeof(RegisterInsuranceProviderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        RegisterInsuranceProviderRequest request,
        [FromServices] ICommandHandler<RegisterInsuranceProviderCommand, int> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, new RegisterInsuranceProviderResponse(result.Value))
            : ErrorResult(result.Error);
    }

    /// <summary>
    /// Lista los proveedores de seguros, paginados y ordenados por nombre.
    /// </summary>
    /// <remarks>
    /// Es público: no requiere sesión. Los proveedores inactivos solo los ve el administrador; para los demás el
    /// parámetro status no amplía el resultado y siempre se devuelven los activos. La búsqueda por nombre es
    /// parcial y no distingue mayúsculas ni tildes.
    /// 400 indica en "code" el error de paginación: InsuranceProvider.InvalidPageNumber,
    /// InsuranceProvider.InvalidPageSize o InsuranceProvider.PageOffsetTooLarge.
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ListInsuranceProvidersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] ListInsuranceProvidersRequest request,
        [FromServices] IQueryHandler<ListInsuranceProvidersQuery, PagedResult<InsuranceProviderDto>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }
}
