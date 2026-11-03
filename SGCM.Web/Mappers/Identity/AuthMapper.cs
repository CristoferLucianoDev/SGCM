using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.Login;
using SGCM.Data.Application.Identity.Commands.ChangeTemporaryPassword;
using SGCM.Data.Application.Identity.Commands.Logout;
using SGCM.Data.Application.Identity.Commands.RefreshSession;
using SGCM.Web.Contracts.Identity;

namespace SGCM.Web.Mappers.Identity;

/// <summary>
/// Conversiones entre los contratos de autenticación y los comandos de Identity.
/// </summary>
public static class AuthMapper
{
    public static ChangeTemporaryPasswordCommand ToCommand(this ChangeTemporaryPasswordRequest request) =>
        new(request.NewPassword);

    /// <summary>
    /// Arma el comando de inicio de sesión con las credenciales del cuerpo y los datos de la petición.
    /// </summary>
    /// <param name="request">Credenciales recibidas.</param>
    /// <param name="deviceName">Nombre del dispositivo, tomado de la petición.</param>
    /// <param name="ipAddress">IP del cliente, tomada de la conexión.</param>
    /// <returns>El comando de inicio de sesión.</returns>
    public static LoginCommand ToCommand(this LoginRequest request, string? deviceName, string? ipAddress) =>
        new(request.Email, request.Password, deviceName, ipAddress);

    /// <summary>
    /// Convierte el resultado del inicio de sesión en la respuesta de la API.
    /// </summary>
    /// <param name="result">Resultado del comando.</param>
    /// <returns>La respuesta, sin tokens si falta el segundo factor.</returns>
    public static LoginResponse ToResponse(this LoginResult result) => result.ToResponse(isMobile: false);

    /// <summary>Adapta únicamente el aviso al cliente declarado, conservando las credenciales.</summary>
    public static LoginResponse ToResponse(this LoginResult result, bool isMobile)
    {
        var tokens = result.Tokens;

        return new LoginResponse(
            result.RequiresTwoFactor,
            tokens?.AccessToken.Value,
            tokens?.AccessToken.ExpiresAt,
            tokens?.RefreshToken,
            tokens?.RefreshTokenExpiresAt,
            tokens?.PendingStep is { } step ? PendingStepClaimValues.From(step) : null,
            RequiresWebFirstAccess: isMobile && !result.RequiresTwoFactor && tokens is not null
                && result.HasPendingDoctorFirstAccess);
    }

    /// <summary>
    /// Convierte los tokens de una sesión recién abierta, por ejemplo al completar el perfil,
    /// en la misma respuesta del inicio de sesión, para que el cliente los trate igual.
    /// </summary>
    /// <param name="tokens">Tokens de la sesión.</param>
    /// <returns>La respuesta con los tokens; nunca pide segundo factor.</returns>
    public static LoginResponse ToResponse(this SessionTokens tokens) =>
        LoginResult.Authenticated(tokens).ToResponse();

    /// <summary>
    /// Arma el comando de renovación de sesión.
    /// </summary>
    /// <param name="request">Datos recibidos.</param>
    /// <returns>El comando de renovación.</returns>
    public static RefreshSessionCommand ToCommand(this RefreshSessionRequest request) =>
        new(request.RefreshToken);

    /// <summary>
    /// Arma el comando de cierre de sesión.
    /// </summary>
    /// <param name="request">Datos recibidos.</param>
    /// <returns>El comando de cierre.</returns>
    public static LogoutCommand ToCommand(this LogoutRequest request) =>
        new(request.RefreshToken);

    /// <summary>
    /// Convierte los tokens de una sesión renovada en la respuesta de la API.
    /// </summary>
    /// <param name="tokens">Tokens de la sesión.</param>
    /// <returns>La respuesta con los tokens nuevos.</returns>
    public static RefreshSessionResponse ToRefreshSessionResponse(this SessionTokens tokens) => new(
        tokens.AccessToken.Value,
        tokens.AccessToken.ExpiresAt,
        tokens.RefreshToken,
        tokens.RefreshTokenExpiresAt,
        tokens.PendingStep is { } step ? PendingStepClaimValues.From(step) : null);
}
