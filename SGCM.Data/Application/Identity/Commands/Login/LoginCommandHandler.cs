using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.Login;

/// <summary>
/// Verifica las credenciales y abre la sesión, o pide el segundo factor si está activo.
/// No guarda: lo hace el decorador de transacción, y la sesión la guarda
/// <see cref="ISessionService"/> dentro de esa misma transacción.
/// </summary>
internal sealed class LoginCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IAttemptLimiter attemptLimiter,
    ITwoFactorStatusReader twoFactorStatusReader,
    ISessionService sessionService,
    IOptions<LoginLimitsOptions> loginLimitsOptions) : ICommandHandler<LoginCommand, LoginResult>
{
    private const string AttemptKeyPrefix = "login:";

    public async Task<Result<LoginResult>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var attemptKey = AttemptKeyPrefix + emailResult.Value.Value;
        if (!TryRegisterAttempt(attemptKey))
        {
            return UserErrors.TooManyLoginAttempts;
        }

        // Bloquea la fila para que dos inicios de sesión simultáneos no rompan el límite de sesiones.
        var user = await userRepository.GetByEmailForUpdateAsync(emailResult.Value, cancellationToken);

        // Se verifica siempre, también sin usuario, para que el tiempo no revele qué cuentas existen.
        // La API no exige los campos por su tipo (la validación es de FluentValidation): una
        // contraseña ausente es una credencial inválida más, así cuenta como intento y responde lo mismo.
        var passwordMatches = passwordHasher.Verify(user?.PasswordHash, command.Password ?? string.Empty);

        // La misma respuesta para correo inexistente, contraseña incorrecta, cuenta pendiente
        // o inactiva, según el PBI, para no revelar el estado de la cuenta.
        if (user is null || !passwordMatches || !user.IsEmailConfirmed || !user.IsActive)
        {
            return UserErrors.InvalidCredentials;
        }

        if (await twoFactorStatusReader.IsEnabledAsync(user.Id, cancellationToken))
        {
            attemptLimiter.Reset(attemptKey);
            return LoginResult.TwoFactorRequired();
        }

        return await StartSessionAsync(user, command, attemptKey, cancellationToken);
    }

    // El intento se registra antes de verificar, para que ninguna petición simultánea se salte
    // el límite; si el inicio de sesión tiene éxito se reinicia. Cuenta también los correos
    // inexistentes, para no revelar cuáles existen.
    private bool TryRegisterAttempt(string attemptKey)
    {
        var limits = loginLimitsOptions.Value;

        return attemptLimiter.TryRegisterAttempt(
            attemptKey,
            limits.MaxFailedAttemptsPerEmail,
            TimeSpan.FromMinutes(limits.EmailWindowMinutes));
    }

    private async Task<Result<LoginResult>> StartSessionAsync(
        User user,
        LoginCommand command,
        string attemptKey,
        CancellationToken cancellationToken)
    {
        var tokensResult = await sessionService.StartAsync(user, command.DeviceName, command.IpAddress, cancellationToken);

        // Sus errores (cuenta inactiva o sin confirmar) ya se descartaron; si apareciera uno,
        // se responde igual para no revelar el estado de la cuenta, y el contador no se reinicia.
        if (tokensResult.IsFailure)
        {
            return UserErrors.InvalidCredentials;
        }

        attemptLimiter.Reset(attemptKey);
        return LoginResult.Authenticated(tokensResult.Value);
    }
}
