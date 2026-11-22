using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Commands.CompleteDoctorProfile;
using SGCM.Data.Application.People.Commands.UpdateDoctorProfile;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Contracts.People;
using SGCM.Web.Mappers.Identity;
using SGCM.Web.Mappers.People;

namespace SGCM.Web.Api.Controllers.People;

[Route("api/doctors")]
public sealed class DoctorProfilesController(ILogger<DoctorProfilesController> logger) : ApiControllerBase
{
    private static readonly Error ProfileCompletionFailed = Error.Failure(
        "Doctor.ProfileCompletionFailed", "Ocurrió un error al completar el perfil.");

    private static readonly Error ProfileUpdateFailed = Error.Failure(
        "Doctor.ProfileUpdateFailed", "Ocurrió un error al actualizar el perfil.");

    private static readonly Error ProfileReadFailed = Error.Failure(
        "Doctor.ProfileReadFailed", "Ocurrió un error al consultar el perfil médico.");

    [HttpGet("{doctorId:int}/profile")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(DoctorPublicProfileResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> GetProfile(
        [FromRoute] int doctorId,
        [FromServices] IQueryHandler<GetDoctorProfileQuery, DoctorProfileResult> handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.HandleAsync(new GetDoctorProfileQuery(doctorId), cancellationToken);
            if (result.IsFailure) return ErrorResult(result.Error);

            return result.Value switch
            {
                DoctorPublicProfileDto profile => Ok(profile.ToResponse()),
                DoctorAdminProfileDto profile => Ok(profile.ToResponse()),
                _ => throw new InvalidOperationException("Variante inesperada del perfil médico.")
            };
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Fallo al consultar el perfil médico ({ExceptionType}). Petición {TraceId}.",
                exception.GetType().Name, HttpContext.TraceIdentifier);
            return ErrorResult(ProfileReadFailed);
        }
    }

    /// <summary>Completa el primer acceso del médico y devuelve los tokens de una sesión nueva.</summary>
    [HttpPost("profile")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [AllowPendingStep(PendingStep.DoctorProfile)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CompleteProfile(
        CompleteDoctorProfileRequest request,
        [FromServices] ICommandHandler<CompleteDoctorProfileCommand, SessionTokens> handler,
        CancellationToken cancellationToken)
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        var deviceName = string.IsNullOrEmpty(userAgent) ? null : userAgent;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var result = await handler.HandleAsync(request.ToCommand(deviceName, ipAddress), cancellationToken);
            return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // La respuesta y el aviso conservan el identificador de la petición sin incluir las credenciales ni el detalle del proveedor.
            logger.LogError("Fallo al completar el perfil médico ({ExceptionType}). Petición {TraceId}.",
                exception.GetType().Name, HttpContext.TraceIdentifier);
            return ErrorResult(ProfileCompletionFailed);
        }
    }

    /// <summary>Reemplaza los datos profesionales editables del médico autenticado.</summary>
    [HttpPut("me/profile")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateProfile(
        UpdateDoctorProfileRequest request,
        [FromServices] ICommandHandler<UpdateDoctorProfileCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);
            return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Fallo al actualizar el perfil médico ({ExceptionType}). Petición {TraceId}.",
                exception.GetType().Name, HttpContext.TraceIdentifier);
            return ErrorResult(ProfileUpdateFailed);
        }
    }
}
