namespace SGCM.Data.Application.Abstractions.Security;

/// <summary>
/// Access token firmado y su vencimiento; el cliente usa el vencimiento para saber
/// cuándo renovarlo.
/// </summary>
/// <param name="Value">Token JWT firmado.</param>
/// <param name="ExpiresAt">Momento en que el token deja de ser válido.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
