using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Commands.CompletePatientProfile;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Contracts.People;
using SGCM.Web.Mappers.Identity;
using SGCM.Web.Mappers.People;

namespace SGCM.Web.Api.Controllers.People;

[Route("api/patients")]
public sealed class PatientsController : ApiControllerBase
{
    /// <summary>
    /// Crea el perfil del paciente autenticado y devuelve tokens nuevos, ya sin paso pendiente.
    /// </summary>
    /// <remarks>
    /// Es el único endpoint que admite el paso pendiente PatientProfile.
    /// 400 si un dato no es válido o el proveedor de seguros no existe o está inactivo.
    /// 409 si el paciente ya tiene perfil.
    /// </remarks>
    [HttpPost("profile")]
    [Authorize(Roles = nameof(UserRole.Patient))]
    [AllowPendingStep(PendingStep.PatientProfile)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CompleteProfile(
        CompletePatientProfileRequest request,
        [FromServices] ICommandHandler<CompletePatientProfileCommand, SessionTokens> handler,
        CancellationToken cancellationToken)
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        var deviceName = string.IsNullOrEmpty(userAgent) ? null : userAgent;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await handler.HandleAsync(request.ToCommand(deviceName, ipAddress), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }
}
