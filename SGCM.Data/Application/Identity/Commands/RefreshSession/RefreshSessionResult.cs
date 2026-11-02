using SGCM.Data.Application.Abstractions.Security;

namespace SGCM.Data.Application.Identity.Commands.RefreshSession;

/// <summary>
/// Resultado de renovar una sesión: los tokens nuevos, o la indicación de que se detectó la
/// reutilización de un token y se revocaron las sesiones del usuario.
/// </summary>
/// <param name="Tokens">Tokens nuevos, o <c>null</c> si se revocaron las sesiones.</param>
/// <param name="SessionRevoked">Indica si se revocaron las sesiones por reutilización del token.</param>
public sealed record RefreshSessionResult(SessionTokens? Tokens, bool SessionRevoked)
{
    /// <summary>
    /// La sesión se renovó.
    /// </summary>
    /// <param name="tokens">Tokens nuevos.</param>
    /// <returns>El resultado con los tokens.</returns>
    public static RefreshSessionResult Renewed(SessionTokens tokens) => new(tokens, false);

    /// <summary>
    /// Se detectó la reutilización del token y se revocaron las sesiones del usuario.
    /// </summary>
    /// <returns>El resultado sin tokens.</returns>
    public static RefreshSessionResult Revoked() => new(null, true);

    /// <summary>
    /// Oculta los tokens, para que no se filtren si el resultado termina en un log.
    /// </summary>
    public override string ToString() =>
        $"{nameof(RefreshSessionResult)} {{ Tokens = ********, SessionRevoked = {SessionRevoked} }}";
}
