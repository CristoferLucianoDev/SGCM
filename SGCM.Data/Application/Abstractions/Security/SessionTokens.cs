namespace SGCM.Data.Application.Abstractions.Security;

/// <summary>
/// Par de tokens que recibe el cliente al abrir una sesión. El refresh token va en claro
/// porque solo se entrega esta vez; en la base queda únicamente su hash.
/// </summary>
/// <param name="AccessToken">Access token JWT firmado y su vencimiento.</param>
/// <param name="RefreshToken">Refresh token en claro.</param>
/// <param name="RefreshTokenExpiresAt">Momento en que vence el refresh token, que es el vencimiento de la sesión.</param>
public sealed record SessionTokens(AccessToken AccessToken, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
