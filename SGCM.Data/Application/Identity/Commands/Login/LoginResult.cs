using SGCM.Data.Application.Abstractions.Security;

namespace SGCM.Data.Application.Identity.Commands.Login;

/// <summary>
/// Resultado de un inicio de sesión con credenciales válidas. Con el segundo factor activo
/// no se emiten tokens: el usuario todavía debe completar ese paso.
/// </summary>
/// <param name="RequiresTwoFactor">Indica si falta el segundo factor.</param>
/// <param name="Tokens">Tokens de la sesión abierta, o <c>null</c> si falta el segundo factor.</param>
public sealed record LoginResult(bool RequiresTwoFactor, SessionTokens? Tokens)
{
    // La tarea "Implementar el segundo paso del inicio de sesión" agregará aquí el desafío que
    // identifica al usuario en ese paso.

    /// <summary>
    /// Sesión abierta, con sus tokens.
    /// </summary>
    /// <param name="tokens">Tokens emitidos para la sesión.</param>
    /// <returns>Un resultado sin segundo factor pendiente.</returns>
    public static LoginResult Authenticated(SessionTokens tokens) => new(false, tokens);

    /// <summary>
    /// Credenciales válidas, pero falta el segundo factor; no se emiten tokens.
    /// </summary>
    /// <returns>Un resultado sin tokens que pide el segundo factor.</returns>
    public static LoginResult TwoFactorRequired() => new(true, null);
}
