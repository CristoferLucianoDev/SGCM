using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Identity.Commands.ConfirmEmail;
using SGCM.Data.Application.Identity.Commands.RegisterPatientAccount;
using SGCM.Data.Application.Identity.Commands.RequestPasswordReset;
using SGCM.Data.Application.Identity.Commands.ResendConfirmationEmail;
using SGCM.Data.Application.Identity.Commands.ResetPassword;
using SGCM.Data.Domain.Common;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Hosting;
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

    /// <summary>
    /// Confirma el correo de una cuenta pendiente con el token del enlace.
    /// </summary>
    /// <remarks>
    /// Es POST y no GET para que los escáneres de enlaces de los servicios de correo
    /// no consuman el token al revisar el mensaje.
    /// </remarks>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ConfirmEmail(
        ConfirmEmailRequest request,
        [FromServices] ICommandHandler<ConfirmEmailCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }

    /// <summary>
    /// Reenvía el correo de confirmación de un registro pendiente con un enlace nuevo,
    /// que invalida el anterior.
    /// </summary>
    /// <remarks>
    /// Respuestas de error:
    /// 400 si el correo no es válido, o si el registro venció o no existe; en ambos casos
    /// el usuario debe registrarse de nuevo.
    /// 403 si la cuenta está inactiva.
    /// 409 si la cuenta ya está confirmada, o si no pasó la espera mínima desde el último envío.
    /// </remarks>
    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResendConfirmationEmail(
        ResendConfirmationEmailRequest request,
        [FromServices] ICommandHandler<ResendConfirmationEmailCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess ? Accepted() : ErrorResult(result.Error);
    }

    /// <summary>
    /// Solicita el enlace para restablecer la contraseña.
    /// </summary>
    /// <remarks>
    /// Responde 202 exista o no la cuenta, para no revelar qué correos están registrados; el
    /// enlace solo se envía si la cuenta existe, está confirmada y está activa.
    /// Respuestas de error:
    /// 400 si el correo no es válido.
    /// 429 por demasiadas solicitudes con el mismo correo o desde la misma IP.
    /// </remarks>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PasswordReset)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request,
        [FromServices] ICommandHandler<RequestPasswordResetCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess ? Accepted() : ErrorResult(result.Error);
    }

    /// <summary>
    /// Establece una contraseña nueva con el token del enlace de recuperación y cierra todas
    /// las sesiones abiertas de la cuenta.
    /// </summary>
    /// <remarks>
    /// Es POST y no GET para que los escáneres de enlaces de los servicios de correo
    /// no consuman el token al revisar el mensaje.
    /// Respuestas de error:
    /// 400 si el enlace no es válido, venció, ya se usó o fue reemplazado, o si la contraseña
    /// nueva no cumple la política.
    /// 403 si la cuenta está inactiva.
    /// </remarks>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        [FromServices] ICommandHandler<ResetPasswordCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }
}