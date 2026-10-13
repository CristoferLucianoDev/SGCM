using System.Net;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Infrastructure.Security;

/// <summary>
/// Implementación de <see cref="ISessionService"/>. Une el agregado <see cref="User"/>,
/// el generador de tokens y el servicio de JWT (arquitectura, sección 14).
/// </summary>
internal sealed class SessionService(
    ISecureTokenGenerator tokenGenerator,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    IUserProfileReader profileReader,
    IDateTimeProvider dateTimeProvider,
    IOptions<JwtOptions> options) : ISessionService
{
    // Tamaño del refresh token que fija la arquitectura.
    private const int RefreshTokenSizeInBytes = 64;

    private readonly JwtOptions _options = options.Value;

    public async Task<Result<SessionTokens>> StartAsync(
        User user,
        string? deviceName,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var device = NormalizeDeviceName(deviceName);
        var ip = NormalizeIpAddress(ipAddress);
        var refreshToken = tokenGenerator.Generate(RefreshTokenSizeInBytes);

        var started = user.StartSession(
            refreshToken.Hash,
            device,
            ip,
            dateTimeProvider.UtcNow,
            TimeSpan.FromDays(_options.RefreshTokenDays),
            _options.MaxActiveSessionsPerUser);

        if (started.IsFailure)
        {
            return started.Error;
        }

        var session = started.Value;

        // La sesión necesita su id para el claim sid; el commit lo hace el decorador de
        // transacción del comando.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await IssueTokensAsync(user, session, refreshToken.Value, cancellationToken);
    }

    private async Task<SessionTokens> IssueTokensAsync(
        User user,
        UserSession session,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var hasProfile = await profileReader.HasProfileAsync(user.Id, user.Role, cancellationToken);
        var pendingStep = user.GetPendingStep(hasProfile);

        var accessToken = tokenService.CreateAccessToken(user.Id, user.Role, session.Id, pendingStep);

        return new SessionTokens(accessToken, refreshToken, session.ExpiresAt, pendingStep);
    }

    // Viene del cliente (por ejemplo, el User-Agent) y puede ser más largo que la columna.
    private static string? NormalizeDeviceName(string? deviceName)
    {
        var trimmed = deviceName?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length <= UserSession.MaxDeviceNameLength)
        {
            return trimmed;
        }

        // No se corta un carácter fuera del BMP por la mitad: el texto quedaría con UTF-16
        // inválido y la base lo rechazaría al guardar.
        var length = char.IsHighSurrogate(trimmed[UserSession.MaxDeviceNameLength - 1])
            ? UserSession.MaxDeviceNameLength - 1
            : UserSession.MaxDeviceNameLength;

        return trimmed[..length];
    }

    // Un valor que no es una IP no se guarda. Tampoco una IPv6 con identificador de zona
    // que no cabe en la columna: rompería el inicio de sesión en vez de solo perder el dato.
    private static string? NormalizeIpAddress(string? ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var parsed))
        {
            return null;
        }

        var canonical = parsed.ToString();

        return canonical.Length <= UserSession.MaxIpAddressLength ? canonical : null;
    }
}
