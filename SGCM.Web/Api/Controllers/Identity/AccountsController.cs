using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Identity.Commands.RegisterPatientAccount;
using SGCM.Data.Domain.Common;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Mappers.Identity;

namespace SGCM.Web.Api.Controllers.Identity;

[Route("api/accounts")]
public sealed class AccountsController : ApiControllerBase
{
    /// <summary>
    /// Registra un paciente con la cuenta pendiente de confirmar y envía el correo de confirmación.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        RegisterPatientRequest request,
        [FromServices] ICommandHandler<RegisterPatientAccountCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess ? Accepted() : ErrorResult(result.Error);
    }
}