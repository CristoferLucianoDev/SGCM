using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Identity.Users;

/// <summary>
/// Sesión de un usuario en un dispositivo. Solo guarda el hash del refresh token;
/// el access token no se almacena. Pertenece al agregado <see cref="User"/>, que es
/// quien la crea y la revoca.
/// </summary>
public sealed class UserSession : Entity
{
    /// <summary>Longitud máxima del nombre del dispositivo.</summary>
    public const int MaxDeviceNameLength = 150;

    /// <summary>Longitud máxima de la dirección IP; alcanza para una IPv6 completa.</summary>
    public const int MaxIpAddressLength = 45;

    /// <summary>
    /// Hash del refresh token vigente de la sesión.
    /// </summary>
    public string RefreshTokenHash { get; private set; } = null!;

    /// <summary>
    /// Hash del refresh token anterior a la última rotación, o <c>null</c> si nunca se rotó.
    /// </summary>
    public string? PreviousRefreshTokenHash { get; private set; }

    /// <summary>
    /// Nombre del dispositivo desde el que se abrió la sesión, si se conoce.
    /// </summary>
    public string? DeviceName { get; private set; }

    /// <summary>
    /// Dirección IP desde la que se abrió la sesión, si se conoce.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Momento a partir del cual la sesión deja de ser válida (exclusivo).
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// Último momento en que se usó la sesión. Decide cuál se revoca al superar el máximo.
    /// </summary>
    public DateTimeOffset LastUsedAt { get; private set; }

    /// <summary>
    /// Momento en que se revocó la sesión, o <c>null</c> si no se revocó.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>
    /// Motivo de la revocación, o <c>null</c> si la sesión no se revocó.
    /// </summary>
    public SessionRevocationReason? RevocationReason { get; private set; }

    private UserSession()
    {
    }

    /// <summary>
    /// Abre una sesión vigente desde <paramref name="now"/> durante <paramref name="lifetime"/>.
    /// Los datos ya vienen validados por <see cref="User.StartSession"/>.
    /// </summary>
    internal static UserSession Start(
        string refreshTokenHash,
        string? deviceName,
        string? ipAddress,
        DateTimeOffset now,
        TimeSpan lifetime) =>
        new()
        {
            RefreshTokenHash = refreshTokenHash,
            DeviceName = deviceName,
            IpAddress = ipAddress,
            ExpiresAt = now + lifetime,
            LastUsedAt = now
        };

    /// <summary>
    /// Indica si la sesión puede usarse en el momento indicado: no fue revocada
    /// y todavía no venció.
    /// </summary>
    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>
    /// Revoca la sesión. Si ya estaba revocada, conserva la fecha y el motivo originales.
    /// </summary>
    internal void Revoke(SessionRevocationReason reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason;
    }
}
