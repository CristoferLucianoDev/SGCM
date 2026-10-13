using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.Login;
using SGCM.Web.Contracts.Identity;

namespace SGCM.Web.Mappers.Identity;

/// <summary>
/// Conversiones entre los contratos de autenticación y los comandos de Identity.
/// </summary>
public static class AuthMapper
{
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
    public static LoginResponse ToResponse(this LoginResult result)
    {
        var tokens = result.Tokens;

        return new LoginResponse(
            result.RequiresTwoFactor,
            tokens?.AccessToken.Value,
            tokens?.AccessToken.ExpiresAt,
            tokens?.RefreshToken,
            tokens?.RefreshTokenExpiresAt,
            tokens?.PendingStep is { } step ? PendingStepClaimValues.From(step) : null);
    }
}
