using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Identity.Commands.Login;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Hosting;
using SGCM.Web.Mappers.Identity;

namespace SGCM.Web.Api.Controllers.Identity;

[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>
    /// Inicia sesión con correo y contraseña y devuelve los tokens de la sesión.
    /// </summary>
    /// <remarks>
    /// 401 es la misma respuesta para correo inexistente, contraseña incorrecta, cuenta
    /// pendiente o inactiva.
    /// 429 aparece por demasiados intentos con el mismo correo o demasiadas peticiones desde
    /// la misma IP.
    /// Con requiresTwoFactor en true no hay tokens y el cliente debe completar el segundo factor.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        LoginRequest request,
        [FromServices] ICommandHandler<LoginCommand, LoginResult> handler,
        CancellationToken cancellationToken)
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        var deviceName = string.IsNullOrEmpty(userAgent) ? null : userAgent;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await handler.HandleAsync(request.ToCommand(deviceName, ipAddress), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }
}
