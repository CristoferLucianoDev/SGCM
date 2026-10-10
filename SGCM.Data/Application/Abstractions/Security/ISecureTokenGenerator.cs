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

    /// <summary>
    /// Calcula el hash del token en claro recibido en un enlace, con el mismo algoritmo
    /// que <see cref="Generate"/>, para poder buscarlo en la base.
    /// </summary>
    /// <param name="rawToken">Token en claro recibido en el enlace.</param>
    /// <returns>El hash del token.</returns>
    string Hash(string rawToken);
}