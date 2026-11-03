namespace SGCM.Web.Contracts.Identity;

/// <summary>
/// Resultado del inicio de sesión. Con <paramref name="RequiresTwoFactor"/> en <c>true</c>
/// no hay tokens: el cliente debe completar el segundo factor.
/// </summary>
/// <param name="RequiresTwoFactor">Indica si falta el segundo factor.</param>
/// <param name="AccessToken">Access token JWT, o <c>null</c> si falta el segundo factor.</param>
/// <param name="AccessTokenExpiresAt">Vencimiento del access token.</param>
/// <param name="RefreshToken">Refresh token en claro; solo se entrega esta vez.</param>
/// <param name="RefreshTokenExpiresAt">Vencimiento del refresh token, que es el de la sesión.</param>
/// <param name="PendingStep">
/// Paso que el usuario debe completar, con los mismos valores que el claim <c>pending_step</c>
/// (<c>PendingStepClaimValues</c>), o <c>null</c> si no hay paso.
/// </param>
/// <param name="RequiresWebFirstAccess">
/// Aviso para el médico móvil con primer acceso pendiente. Se omite si es false para conservar
/// el JSON anterior. Es una indicación de interfaz, no una acreditación del origen del cliente.
/// </param>
public sealed record LoginResponse(
    bool RequiresTwoFactor,
    string? AccessToken,
    DateTimeOffset? AccessTokenExpiresAt,
    string? RefreshToken,
    DateTimeOffset? RefreshTokenExpiresAt,
    string? PendingStep,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool RequiresWebFirstAccess = false);
