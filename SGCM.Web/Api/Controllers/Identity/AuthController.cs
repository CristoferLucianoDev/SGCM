using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Identity.Commands.Login;
using SGCM.Data.Application.Identity.Commands.ChangeTemporaryPassword;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.Logout;
using SGCM.Data.Application.Identity.Commands.RefreshSession;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Contracts.Common;
using SGCM.Web.Hosting;
using SGCM.Web.Mappers.Identity;

namespace SGCM.Web.Api.Controllers.Identity;

[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    private static readonly Error InvalidClientType = Error.Validation(
        "Request.InvalidClientType", "El tipo de cliente debe ser web o mobile, con un único valor.");

    /// <summary>Cambia la contraseña temporal y renueva las credenciales de la misma sesión.</summary>
    [HttpPost("change-temporary-password")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [AllowPendingStep(PendingStep.ChangeTemporaryPassword)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeTemporaryPassword(
        ChangeTemporaryPasswordRequest request,
        [FromServices] ICommandHandler<ChangeTemporaryPasswordCommand, SessionTokens> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }

    /// <summary>
    /// Inicia sesión con correo y contraseña y devuelve los tokens de la sesión.
    /// </summary>
    /// <remarks>
    /// 401 es la misma respuesta para correo inexistente, contraseña incorrecta, cuenta
    /// pendiente o inactiva.
    /// 429 aparece por demasiados intentos con el mismo correo o demasiadas peticiones desde
    /// la misma IP.
    /// Con requiresTwoFactor en true no hay tokens y el cliente debe completar el segundo factor.
    /// X-SGCM-Client admite web o mobile (sin distinguir mayúsculas y con espacios exteriores).
    /// Ausente conserva web; valores vacíos, desconocidos o múltiples responden 400 antes del comando.
    /// El médico móvil con paso pendiente recibe requiresWebFirstAccess: true y los tokens habituales.
    /// La cabecera es declarativa: no acredita procedencia ni modifica la autorización.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    // La respuesta lleva tokens: ningún intermediario ni el navegador deben guardarla.
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        LoginRequest request,
        [FromServices] ICommandHandler<LoginCommand, LoginResult> handler,
        CancellationToken cancellationToken)
    {
        var client = ReadMobileClient();
        if (client.IsFailure) return ErrorResult(client.Error);

        var userAgent = Request.Headers.UserAgent.ToString();
        var deviceName = string.IsNullOrEmpty(userAgent) ? null : userAgent;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await handler.HandleAsync(request.ToCommand(deviceName, ipAddress), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse(isMobile: client.Value)) : ErrorResult(result.Error);
    }

    private Result<bool> ReadMobileClient()
    {
        if (!Request.Headers.TryGetValue(ClientTypeHeader.Name, out var values)) return false;
        if (values.Count != 1) return InvalidClientType;

        var value = values[0]?.Trim();
        if (string.Equals(value, ClientTypeHeader.Web, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(value, ClientTypeHeader.Mobile, StringComparison.OrdinalIgnoreCase)) return true;
        return InvalidClientType;
    }

    /// <summary>
    /// Renueva la sesión con su refresh token y devuelve los tokens nuevos. El refresh token
    /// presentado deja de servir, así que el cliente debe guardar el nuevo.
    /// </summary>
    /// <remarks>
    /// Es anónimo porque se usa justamente cuando el access token ya venció.
    /// Ante un 401 el cliente debe enviar al usuario al inicio de sesión y no reintentar.
    /// </remarks>
    /// <param name="request">Refresh token de la sesión.</param>
    /// <param name="handler">Caso de uso de la renovación.</param>
    /// <returns>
    /// 200 con los tokens nuevos, 400 si falta el token o 401 si la sesión no se puede renovar.
    /// </returns>
    [HttpPost("refresh")]
    [AllowAnonymous]
    // La respuesta lleva tokens: ningún intermediario ni el navegador deben guardarla.
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(RefreshSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        RefreshSessionRequest request,
        [FromServices] ICommandHandler<RefreshSessionCommand, RefreshSessionResult> handler)
    {
        // Sin la cancelación de la petición: si el cliente corta antes del commit, la revocación
        // por robo se revertiría, y quien presenta un token robado podría provocarlo a propósito.
        // Es una operación corta, así que termina aunque el cliente se desconecte.
        var result = await handler.HandleAsync(request.ToCommand(), CancellationToken.None);

        if (result.IsFailure)
        {
            return ErrorResult(result.Error);
        }

        // La misma respuesta que una sesión inválida, para no revelar que se detectó la reutilización.
        if (result.Value.SessionRevoked)
        {
            return ErrorResult(UserErrors.SessionInvalid);
        }

        return Ok(result.Value.Tokens!.ToRefreshSessionResponse());
    }

    /// <summary>
    /// Cierra la sesión del refresh token recibido.
    /// </summary>
    /// <remarks>
    /// Es anónimo para que un usuario con el access token vencido pueda cerrar su sesión.
    /// Responde 204 aunque el token no exista o ya esté cerrado.
    /// </remarks>
    /// <param name="request">Refresh token de la sesión.</param>
    /// <param name="handler">Caso de uso del cierre de sesión.</param>
    /// <returns>204 si termina bien, o 400 si falta el token.</returns>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout(
        LogoutRequest request,
        [FromServices] ICommandHandler<LogoutCommand, Unit> handler)
    {
        // Sin la cancelación de la petición, para que un usuario que pierde la conexión no quede
        // con la sesión abierta sin saberlo. Es una operación corta, así que termina aunque el
        // cliente se desconecte.
        var result = await handler.HandleAsync(request.ToCommand(), CancellationToken.None);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }
}
