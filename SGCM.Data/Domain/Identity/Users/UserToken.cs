using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Identity.Users;

/// <summary>
/// Token de un solo uso enviado por correo al usuario. Solo se guarda su hash.
/// Pertenece al agregado <see cref="User"/>, que es quien lo crea e invalida.
/// </summary>
public sealed class UserToken : Entity
{
    /// <summary>
    /// Propósito del token.
    /// </summary>
    public UserTokenType Type { get; private set; }

    /// <summary>
    /// Hash del token. El valor en claro solo viaja en el correo.
    /// </summary>
    public string TokenHash { get; private set; } = null!;

    /// <summary>
    /// Momento de la emisión.
    /// </summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>
    /// Momento a partir del cual el token deja de ser válido (exclusivo).
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// Momento en que se usó el token, o <c>null</c> si no se usó.
    /// </summary>
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>
    /// Momento en que se invalidó el token por emitirse otro del mismo tipo,
    /// o <c>null</c> si no se invalidó.
    /// </summary>
    public DateTimeOffset? InvalidatedAt { get; private set; }

    private UserToken()
    {
    }

    /// <summary>
    /// Crea un token vigente desde <paramref name="now"/> durante <paramref name="lifetime"/>.
    /// Los datos ya vienen validados por <see cref="User.IssueToken"/>.
    /// </summary>
    internal static UserToken Issue(UserTokenType type, string tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new()
        {
            Type = type,
            TokenHash = tokenHash,
            IssuedAt = now,
            ExpiresAt = now + lifetime
        };

    /// <summary>
    /// Indica si el token está pendiente: no fue usado ni invalidado, aunque ya haya vencido.
    /// Es la misma condición del índice único parcial <c>uq_user_token_user_id_token_type_id_active</c>,
    /// así que solo puede haber un token pendiente por usuario y tipo.
    /// </summary>
    public bool IsPending => UsedAt is null && InvalidatedAt is null;

    /// <summary>
    /// Indica si el token puede usarse en el momento indicado: no fue usado,
    /// no fue invalidado y todavía no venció.
    /// </summary>
    public bool IsValidAt(DateTimeOffset now) =>
        IsPending && ExpiresAt > now;

    /// <summary>
    /// Invalida el token. Si ya estaba invalidado, conserva la fecha original.
    /// </summary>
    internal void Invalidate(DateTimeOffset now) => InvalidatedAt ??= now;

    /// <summary>
    /// Marca el token como usado. Lo llama <see cref="User.ConsumeToken"/> después
    /// de validar que el token es válido.
    /// </summary>
    internal void MarkUsed(DateTimeOffset now) => UsedAt = now;
}
