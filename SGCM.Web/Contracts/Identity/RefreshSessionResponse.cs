namespace SGCM.Web.Contracts.Identity;

/// <summary>
/// Tokens de una sesión recién renovada.
/// </summary>
/// <param name="AccessToken">Access token JWT nuevo.</param>
/// <param name="AccessTokenExpiresAt">Momento en que vence el access token.</param>
/// <param name="RefreshToken">Refresh token nuevo, que reemplaza al presentado.</param>
/// <param name="RefreshTokenExpiresAt">Momento en que vence el refresh token.</param>
/// <param name="PendingStep">Paso que el usuario debe completar, o <c>null</c> si no tiene ninguno.</param>
public sealed record RefreshSessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string? PendingStep)
{
    /// <summary>
    /// Oculta los tokens, para que no se filtren si la respuesta termina en un log.
    /// </summary>
    public override string ToString() =>
        $"{nameof(RefreshSessionResponse)} {{ AccessToken = ********, RefreshToken = ********, PendingStep = {PendingStep} }}";
}
