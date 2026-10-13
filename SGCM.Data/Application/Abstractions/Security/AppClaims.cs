namespace SGCM.Data.Application.Abstractions.Security;

/// <summary>
/// Nombres de los claims del access token. Los comparten quien emite el token y quien
/// lee al usuario actual, para que ambos lados usen exactamente los mismos nombres.
/// </summary>
public static class AppClaims
{
    /// <summary>Id del usuario dueño del token.</summary>
    public const string UserId = "sub";

    /// <summary>Rol del usuario.</summary>
    public const string Role = "role";

    /// <summary>Id de la sesión que emitió el token.</summary>
    public const string SessionId = "sid";

    /// <summary>Identificador único del token.</summary>
    public const string TokenId = "jti";
}
