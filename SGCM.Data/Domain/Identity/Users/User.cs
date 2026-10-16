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
    private readonly List<UserSession> _sessions = [];

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

    /// <summary>
    /// Historial de sesiones abiertas, activas o no.
    /// </summary>
    public IReadOnlyCollection<UserSession> Sessions => _sessions.AsReadOnly();

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
    /// Reenvía el correo de confirmación de un registro pendiente: emite un token de
    /// confirmación nuevo, lo que invalida el anterior, siempre que haya pasado la espera
    /// mínima desde el último enlace enviado. El vencimiento del registro se revisa antes
    /// que la espera porque, si el registro venció, esperar no sirve: hay que registrarse de nuevo.
    /// </summary>
    /// <param name="rawToken">Nuevo token de confirmación en claro.</param>
    /// <param name="tokenHash">Hash del nuevo token de confirmación.</param>
    /// <param name="now">Momento del reenvío.</param>
    /// <param name="confirmationLifetime">Tiempo de vigencia del nuevo token.</param>
    /// <param name="resendCooldown">Espera mínima entre el último enlace enviado y un reenvío.</param>
    /// <returns>
    /// Éxito; <see cref="UserErrors.EmailAlreadyConfirmed"/> si la cuenta ya fue confirmada;
    /// <see cref="UserErrors.AccountInactive"/> si la cuenta está desactivada;
    /// <see cref="UserErrors.RegistrationExpired"/> si el registro pendiente venció;
    /// o <see cref="UserErrors.ResendTooSoon"/> si no pasó la espera mínima.
    /// En todos los casos de error el usuario no se modifica.
    /// </returns>
    /// <exception cref="DomainException">Si falta algún dato obligatorio o la vigencia no es positiva.</exception>
    public Result ResendEmailConfirmation(
        string rawToken,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan confirmationLifetime,
        TimeSpan resendCooldown)
    {
        if (IsEmailConfirmed)
        {
            return UserErrors.EmailAlreadyConfirmed;
        }

        if (!IsActive)
        {
            return UserErrors.AccountInactive;
        }

        if (IsPendingRegistrationExpiredAt(now))
        {
            return UserErrors.RegistrationExpired;
        }

        if (now < LastEmailConfirmationIssuedAt() + resendCooldown)
        {
            return UserErrors.ResendTooSoon;
        }

        IssueToken(UserTokenType.EmailConfirmation, rawToken, tokenHash, now, confirmationLifetime);

        return Result.Success();
    }

    /// <summary>
    /// Emite un token del tipo indicado. Antes invalida los tokens pendientes (no usados ni
    /// invalidados) del mismo tipo, aunque ya hayan vencido, de modo que solo el último enlace
    /// enviado funcione. Incluye los vencidos porque así lo exige el índice único
    /// <c>uq_user_token_user_id_token_type_id_active</c>, que admite un solo token pendiente
    /// por usuario y tipo sin mirar el vencimiento. Los tokens de otros tipos no se tocan.
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

        foreach (var token in _tokens.Where(token => token.Type == type && token.IsPending))
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
        var found = FindUsableToken(type, tokenHash, now);

        if (found.IsFailure)
        {
            return found.Error;
        }

        found.Value.MarkUsed(now);

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

    /// <summary>
    /// Indica si el token de recuperación permite restablecer la contraseña, con las mismas
    /// reglas y en el mismo orden que <see cref="ResetPassword"/>. No modifica al usuario: sirve
    /// para descartar un enlace inválido antes de calcular el hash de la contraseña nueva.
    /// </summary>
    /// <param name="tokenHash">Hash del token de recuperación recibido en el enlace.</param>
    /// <param name="now">Momento de la consulta.</param>
    /// <returns>
    /// Éxito; <see cref="UserErrors.AccountInactive"/> si la cuenta está desactivada;
    /// <see cref="UserErrors.TokenInvalid"/> si la cuenta no confirmó su correo o si el token
    /// no existe, ya se usó o fue reemplazado; o <see cref="UserErrors.TokenExpired"/> si venció.
    /// </returns>
    public Result CanResetPassword(string tokenHash, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return UserErrors.AccountInactive;
        }

        // El enlace solo se emite a cuentas confirmadas; si llega uno de otra, no se acepta.
        if (!IsEmailConfirmed)
        {
            return UserErrors.TokenInvalid;
        }

        var found = FindUsableToken(UserTokenType.PasswordReset, tokenHash, now);

        return found.IsFailure ? found.Error : Result.Success();
    }

    /// <summary>
    /// Restablece la contraseña consumiendo el token de recuperación recibido en el enlace, y
    /// revoca con <see cref="SessionRevocationReason.PasswordReset"/> todas las sesiones activas.
    /// </summary>
    /// <param name="tokenHash">Hash del token de recuperación recibido en el enlace.</param>
    /// <param name="newPasswordHash">Hash de la contraseña nueva, ya validada.</param>
    /// <param name="now">Momento del restablecimiento.</param>
    /// <returns>
    /// El mismo error que <see cref="CanResetPassword"/>, que se evalúa antes de modificar nada.
    /// En todos los casos de error el usuario no se modifica.
    /// </returns>
    /// <exception cref="DomainException">Si falta el hash de la contraseña nueva.</exception>
    public Result ResetPassword(string tokenHash, string newPasswordHash, DateTimeOffset now)
    {
        EnsurePasswordHash(newPasswordHash);

        var allowed = CanResetPassword(tokenHash, now);

        if (allowed.IsFailure)
        {
            return allowed;
        }

        var consumed = ConsumeToken(UserTokenType.PasswordReset, tokenHash, now);

        // CanResetPassword ya validó el mismo token; se revisa igual para que la contraseña
        // nunca cambie sin consumir el enlace si las dos validaciones llegaran a divergir.
        if (consumed.IsFailure)
        {
            return consumed;
        }

        PasswordHash = newPasswordHash;

        // El usuario eligió su propia contraseña, así que el paso de contraseña temporal queda cumplido.
        MustChangePassword = false;

        RevokeActiveSessions(SessionRevocationReason.PasswordReset, now);

        return Result.Success();
    }

    /// <summary>
    /// Abre una sesión nueva en un dispositivo. Si el usuario ya tiene el máximo de sesiones
    /// activas, revoca con <see cref="SessionRevocationReason.SessionLimit"/> las usadas hace
    /// más tiempo hasta dejar lugar para la nueva.
    /// </summary>
    /// <param name="refreshTokenHash">Hash del refresh token de la sesión, que es lo que se persiste.</param>
    /// <param name="deviceName">Nombre del dispositivo, si se conoce.</param>
    /// <param name="ipAddress">Dirección IP desde la que se inicia la sesión, si se conoce.</param>
    /// <param name="now">Momento del inicio de sesión.</param>
    /// <param name="lifetime">Tiempo de vigencia de la sesión.</param>
    /// <param name="maxActiveSessions">Máximo de sesiones activas por usuario, contando la nueva.</param>
    /// <returns>
    /// La sesión creada; <see cref="UserErrors.AccountInactive"/> si la cuenta está desactivada;
    /// o <see cref="UserErrors.EmailNotConfirmed"/> si la cuenta no confirmó su correo.
    /// En ambos casos de error el usuario no se modifica.
    /// </returns>
    /// <exception cref="DomainException">
    /// Si falta el hash, el dispositivo o la IP superan su longitud máxima, la vigencia no es
    /// positiva o el máximo de sesiones es menor que uno.
    /// </exception>
    public Result<UserSession> StartSession(
        string refreshTokenHash,
        string? deviceName,
        string? ipAddress,
        DateTimeOffset now,
        TimeSpan lifetime,
        int maxActiveSessions)
    {
        EnsureSessionData(refreshTokenHash, deviceName, ipAddress, lifetime, maxActiveSessions);

        if (!IsActive)
        {
            return UserErrors.AccountInactive;
        }

        if (!IsEmailConfirmed)
        {
            return UserErrors.EmailNotConfirmed;
        }

        RevokeSessionsOverLimit(now, maxActiveSessions);

        var session = UserSession.Start(refreshTokenHash, deviceName, ipAddress, now, lifetime);
        _sessions.Add(session);

        return session;
    }

    /// <summary>
    /// Indica el paso que el usuario debe completar antes de usar el resto del sistema.
    /// La contraseña temporal va primero, porque el médico la cambia antes de completar su
    /// perfil. El administrador no tiene paso de perfil.
    /// </summary>
    /// <param name="hasProfile">
    /// Si el usuario ya tiene su perfil de médico o de paciente. El perfil está en otro
    /// agregado, así que lo resuelve quien llama.
    /// </param>
    /// <returns>El paso pendiente, o <c>null</c> si no tiene ninguno.</returns>
    /// <exception cref="DomainException">Si el rol del usuario no tiene definido su paso de perfil.</exception>
    public PendingStep? GetPendingStep(bool hasProfile)
    {
        if (MustChangePassword)
        {
            return PendingStep.ChangeTemporaryPassword;
        }

        if (hasProfile)
        {
            return null;
        }

        return Role switch
        {
            UserRole.Doctor => PendingStep.DoctorProfile,
            UserRole.Patient => PendingStep.PatientProfile,
            UserRole.Administrator => null,
            _ => throw new DomainException($"El rol {Role} no tiene definido su paso de perfil.")
        };
    }

    // Bucle y no un solo if: si se baja el máximo en la configuración, un usuario puede
    // tener más de una sesión de sobra.
    private void RevokeSessionsOverLimit(DateTimeOffset now, int maxActiveSessions)
    {
        var active = _sessions.Where(session => session.IsActiveAt(now)).ToList();

        while (active.Count >= maxActiveSessions)
        {
            var leastRecentlyUsed = active.MinBy(session => session.LastUsedAt)!;
            leastRecentlyUsed.Revoke(SessionRevocationReason.SessionLimit, now);
            active.Remove(leastRecentlyUsed);
        }
    }

    // Busca el token y valida que se pueda usar, sin modificarlo.
    private Result<UserToken> FindUsableToken(UserTokenType type, string tokenHash, DateTimeOffset now)
    {
        // El hash viene de fuera: si es nulo o vacío simplemente no coincide con ninguno.
        var token = _tokens.FirstOrDefault(token => token.Type == type && token.TokenHash == tokenHash);

        if (token is null || !token.IsPending)
        {
            return UserErrors.TokenInvalid;
        }

        if (token.ExpiresAt <= now)
        {
            return UserErrors.TokenExpired;
        }

        return token;
    }

    // Las sesiones ya revocadas conservan su fecha y motivo originales.
    private void RevokeActiveSessions(SessionRevocationReason reason, DateTimeOffset now)
    {
        foreach (var session in _sessions.Where(session => session.IsActiveAt(now)))
        {
            session.Revoke(reason, now);
        }
    }

    // La espera se mide desde el último enlace enviado, sea por registro, reemplazo o reenvío.
    private DateTimeOffset LastEmailConfirmationIssuedAt() =>
        _tokens
            .Where(token => token.Type == UserTokenType.EmailConfirmation)
            .Max(token => token.IssuedAt);

    private static void EnsurePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("El hash de la contraseña es obligatorio.");
        }
    }

    private static void EnsureSessionData(
        string refreshTokenHash,
        string? deviceName,
        string? ipAddress,
        TimeSpan lifetime,
        int maxActiveSessions)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenHash))
        {
            throw new DomainException("El hash del refresh token es obligatorio.");
        }

        if (deviceName?.Length > UserSession.MaxDeviceNameLength)
        {
            throw new DomainException($"El nombre del dispositivo no puede superar {UserSession.MaxDeviceNameLength} caracteres.");
        }

        if (ipAddress?.Length > UserSession.MaxIpAddressLength)
        {
            throw new DomainException($"La dirección IP no puede superar {UserSession.MaxIpAddressLength} caracteres.");
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainException("La vigencia de la sesión debe ser positiva.");
        }

        if (maxActiveSessions < 1)
        {
            throw new DomainException("El máximo de sesiones activas debe ser al menos uno.");
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
