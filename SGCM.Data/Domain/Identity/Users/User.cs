using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users.Events;

namespace SGCM.Data.Domain.Identity.Users;

/// <summary>
/// Cuenta de acceso al sistema. Es la raíz del agregado que agrupa sus tokens
/// de confirmación de correo y de restablecimiento de contraseña.
/// </summary>
public sealed class User : ActivatableAggregateRoot
{
    private readonly List<UserToken> _tokens = [];

    /// <summary>
    /// Correo con el que el usuario inicia sesión.
    /// </summary>
    public Email Email { get; private set; } = null!;

    /// <summary>
    /// Hash de la contraseña. La contraseña en claro nunca llega al agregado.
    /// </summary>
    public string PasswordHash { get; private set; } = null!;

    /// <summary>
    /// Rol que determina los permisos del usuario.
    /// </summary>
    public UserRole Role { get; private set; }

    /// <summary>
    /// Indica si el usuario confirmó su correo. Mientras sea <c>false</c>, la cuenta está pendiente.
    /// </summary>
    public bool IsEmailConfirmed { get; private set; }

    /// <summary>
    /// Indica si el usuario debe cambiar su contraseña en el próximo inicio de sesión.
    /// </summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>
    /// Historial de tokens emitidos, vigentes o no.
    /// </summary>
    public IReadOnlyCollection<UserToken> Tokens => _tokens.AsReadOnly();

    /// <inheritdoc />
    protected override string EntityName => nameof(User);

    private User()
    {
    }

    /// <summary>
    /// Registra un paciente con la cuenta pendiente de confirmar y le emite el token
    /// de confirmación de correo.
    /// </summary>
    /// <param name="email">Correo del paciente.</param>
    /// <param name="passwordHash">Hash de la contraseña elegida.</param>
    /// <param name="rawToken">Token de confirmación en claro, que viaja en el evento para enviarse por correo.</param>
    /// <param name="tokenHash">Hash del token de confirmación, que es lo que se persiste.</param>
    /// <param name="now">Momento del registro.</param>
    /// <param name="confirmationLifetime">Tiempo de vigencia del token de confirmación.</param>
    /// <returns>El usuario pendiente, con un <see cref="UserTokenIssuedEvent"/> registrado.</returns>
    /// <exception cref="DomainException">Si falta algún dato obligatorio o la vigencia no es positiva.</exception>
    public static User RegisterPending(
        Email email,
        string passwordHash,
        string rawToken,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan confirmationLifetime)
    {
        ArgumentNullException.ThrowIfNull(email);
        EnsurePasswordHash(passwordHash);

        var user = new User
        {
            Email = email,
            PasswordHash = passwordHash,
            Role = UserRole.Patient
        };

        user.IssueToken(UserTokenType.EmailConfirmation, rawToken, tokenHash, now, confirmationLifetime);

        return user;
    }

    /// <summary>
    /// Reemplaza un registro que nunca se confirmó cuando alguien vuelve a registrarse
    /// con el mismo correo: actualiza la contraseña e invalida el token de confirmación
    /// anterior al emitir uno nuevo.
    /// </summary>
    /// <param name="passwordHash">Hash de la nueva contraseña.</param>
    /// <param name="rawToken">Nuevo token de confirmación en claro.</param>
    /// <param name="tokenHash">Hash del nuevo token de confirmación.</param>
    /// <param name="now">Momento del nuevo registro.</param>
    /// <param name="confirmationLifetime">Tiempo de vigencia del nuevo token.</param>
    /// <returns>
    /// Éxito; <see cref="UserErrors.EmailAlreadyInUse"/> si la cuenta ya fue confirmada;
    /// o <see cref="UserErrors.AccountInactive"/> si la cuenta está desactivada.
    /// En ambos casos de error el usuario no se modifica.
    /// </returns>
    /// <exception cref="DomainException">Si falta algún dato obligatorio o la vigencia no es positiva.</exception>
    public Result ReplacePendingRegistration(
        string passwordHash,
        string rawToken,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan confirmationLifetime)
    {
        if (IsEmailConfirmed)
        {
            return UserErrors.EmailAlreadyInUse;
        }

        if (!IsActive)
        {
            return UserErrors.AccountInactive;
        }

        EnsurePasswordHash(passwordHash);
        EnsureTokenData(rawToken, tokenHash, confirmationLifetime);

        PasswordHash = passwordHash;
        IssueToken(UserTokenType.EmailConfirmation, rawToken, tokenHash, now, confirmationLifetime);

        return Result.Success();
    }

    /// <summary>
    /// Emite un token del tipo indicado. Antes invalida los tokens vigentes del mismo
    /// tipo, de modo que solo el último enlace enviado funcione. Los tokens de otros
    /// tipos no se tocan.
    /// </summary>
    /// <param name="type">Propósito del token.</param>
    /// <param name="rawToken">Token en claro, que viaja en el evento para enviarse por correo.</param>
    /// <param name="tokenHash">Hash del token, que es lo que se persiste.</param>
    /// <param name="now">Momento de la emisión.</param>
    /// <param name="lifetime">Tiempo de vigencia del token.</param>
    /// <exception cref="DomainException">Si falta algún dato obligatorio o la vigencia no es positiva.</exception>
    public void IssueToken(UserTokenType type, string rawToken, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        EnsureTokenData(rawToken, tokenHash, lifetime);

        foreach (var token in _tokens.Where(token => token.Type == type && token.IsValidAt(now)))
        {
            token.Invalidate(now);
        }

        _tokens.Add(UserToken.Issue(type, tokenHash, now, lifetime));
        Raise(new UserTokenIssuedEvent(Email.Value, type, rawToken));
    }

    /// <summary>
    /// Consume el token del tipo indicado cuyo hash coincide, marcándolo como usado
    /// para que el mismo enlace no funcione dos veces.
    /// </summary>
    /// <param name="type">Propósito del token.</param>
    /// <param name="tokenHash">Hash del token recibido en el enlace.</param>
    /// <param name="now">Momento del consumo.</param>
    /// <returns>
    /// Éxito; <see cref="UserErrors.TokenInvalid"/> si el token no existe, ya se usó
    /// o fue reemplazado; o <see cref="UserErrors.TokenExpired"/> si ya venció.
    /// En ambos casos de error el token no se modifica.
    /// </returns>
    public Result ConsumeToken(UserTokenType type, string tokenHash, DateTimeOffset now)
    {
        // El hash viene de fuera: si es nulo o vacío simplemente no coincide con ninguno.
        var token = _tokens.FirstOrDefault(token => token.Type == type && token.TokenHash == tokenHash);

        if (token is null || token.UsedAt is not null || token.InvalidatedAt is not null)
        {
            return UserErrors.TokenInvalid;
        }

        if (token.ExpiresAt <= now)
        {
            return UserErrors.TokenExpired;
        }

        token.MarkUsed(now);

        return Result.Success();
    }

    /// <summary>
    /// Indica si el registro pendiente venció. La vida del registro pendiente la marca
    /// el último token de confirmación emitido; cuando ninguno está vigente, el registro
    /// se trata como inexistente aunque la fila siga en la base.
    /// </summary>
    /// <param name="now">Momento de la consulta.</param>
    /// <returns>
    /// <c>true</c> si la cuenta no está confirmada y no tiene ningún token de confirmación
    /// vigente en <paramref name="now"/>; <c>false</c> en cualquier otro caso.
    /// </returns>
    public bool IsPendingRegistrationExpiredAt(DateTimeOffset now) =>
        !IsEmailConfirmed
        && !_tokens.Any(token => token.Type == UserTokenType.EmailConfirmation && token.IsValidAt(now));

    /// <summary>
    /// Confirma el correo de la cuenta consumiendo el token de confirmación recibido en el enlace.
    /// </summary>
    /// <param name="tokenHash">Hash del token de confirmación recibido en el enlace.</param>
    /// <param name="now">Momento de la confirmación.</param>
    /// <returns>
    /// Éxito; <see cref="UserErrors.AccountInactive"/> si la cuenta está desactivada;
    /// <see cref="UserErrors.RegistrationExpired"/> si el registro pendiente venció;
    /// o <see cref="UserErrors.TokenInvalid"/> o <see cref="UserErrors.TokenExpired"/>
    /// según lo que devuelva <see cref="ConsumeToken"/>.
    /// En todos los casos de error el usuario no se modifica.
    /// </returns>
    public Result ConfirmEmail(string tokenHash, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return UserErrors.AccountInactive;
        }

        if (IsPendingRegistrationExpiredAt(now))
        {
            return UserErrors.RegistrationExpired;
        }

        var consumed = ConsumeToken(UserTokenType.EmailConfirmation, tokenHash, now);

        if (consumed.IsFailure)
        {
            return consumed;
        }

        IsEmailConfirmed = true;

        return Result.Success();
    }

    private static void EnsurePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("El hash de la contraseña es obligatorio.");
        }
    }

    private static void EnsureTokenData(string rawToken, string tokenHash, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw new DomainException("El token es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("El hash del token es obligatorio.");
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainException("La vigencia del token debe ser positiva.");
        }
    }
}
