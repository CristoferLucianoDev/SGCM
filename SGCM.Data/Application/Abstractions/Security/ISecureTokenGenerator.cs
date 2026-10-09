namespace SGCM.Data.Application.Abstractions.Security;

/// <summary>
/// Genera tokens de un solo uso criptográficamente seguros.
/// </summary>
public interface ISecureTokenGenerator
{
    /// <summary>
    /// Genera un token nuevo junto con el hash que se persiste.
    /// </summary>
    SecureToken Generate();
}