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
    /// Genera un token de <paramref name="byteCount"/> bytes aleatorios junto con el hash
    /// que se persiste. Los refresh tokens usan 64.
    /// </summary>
    /// <param name="byteCount">Cantidad de bytes aleatorios del token.</param>
    /// <returns>El token en claro y su hash.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="byteCount"/> es menor que 32.</exception>
    SecureToken Generate(int byteCount);

    /// <summary>
    /// Calcula el hash del token en claro recibido en un enlace, con el mismo algoritmo
    /// que <see cref="Generate"/>, para poder buscarlo en la base.
    /// </summary>
    /// <param name="rawToken">Token en claro recibido en el enlace.</param>
    /// <returns>El hash del token.</returns>
    string Hash(string rawToken);
}