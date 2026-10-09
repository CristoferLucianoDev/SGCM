namespace SGCM.Data.Application.Abstractions.Security;

/// <summary>
/// Token aleatorio en texto (se envía al usuario) y su hash (se guarda en la base).
/// </summary>
public sealed record SecureToken(string Value, string Hash);