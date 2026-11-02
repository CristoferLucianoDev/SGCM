namespace SGCM.Web.Contracts.Identity;

/// <summary>
/// Datos para renovar una sesión.
/// </summary>
/// <param name="RefreshToken">Refresh token vigente de la sesión.</param>
public sealed record RefreshSessionRequest(string RefreshToken)
{
    /// <summary>
    /// Oculta el token, para que no se filtre si la petición termina en un log.
    /// </summary>
    public override string ToString() => $"{nameof(RefreshSessionRequest)} {{ RefreshToken = ******** }}";
}
