using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Infrastructure.Security;

/// <summary>
/// Implementación de <see cref="ITokenService"/> con JWT. El token no se guarda en la base:
/// la API lo valida solo con la firma (arquitectura, sección 14).
/// </summary>
internal sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenService(IOptions<JwtOptions> options, IDateTimeProvider dateTimeProvider)
    {
        _options = options.Value;
        _dateTimeProvider = dateTimeProvider;

        // Las opciones no cambian en ejecución, así que la llave se arma una sola vez.
        // SigningKey nunca es nulo aquí: JwtOptionsValidator lo exige al arrancar.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey!));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public AccessToken CreateAccessToken(int userId, UserRole role, int sessionId)
    {
        var now = _dateTimeProvider.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _signingCredentials,
            Claims = new Dictionary<string, object>
            {
                [AppClaims.UserId] = userId.ToString(CultureInfo.InvariantCulture),
                [AppClaims.Role] = role.ToString(),
                [AppClaims.SessionId] = sessionId.ToString(CultureInfo.InvariantCulture),
                [AppClaims.TokenId] = Guid.NewGuid().ToString(),
            },
        });

        return new AccessToken(token, expiresAt);
    }
}
