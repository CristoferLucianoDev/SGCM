using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Fakes;

namespace SGCM.Test.Unit.Infrastructure.Security;

/// <summary>
/// Pruebas de la emisión del access token JWT: claims, vigencia, emisor, audiencia y firma.
/// </summary>
public sealed class JwtTokenServiceTests
{
    private const string Issuer = "SGCM";
    private const string Audience = "SGCM";
    private const int AccessTokenMinutes = 15;
    private const int UserId = 7;
    private const int SessionId = 42;
    private const string ExpectedAlgorithm = "HS256";

    private static readonly string SigningKey = new('k', JwtOptions.MinimumSigningKeyBytes);
    private static readonly string OtherSigningKey = new('x', JwtOptions.MinimumSigningKeyBytes);

    private readonly FakeDateTimeProvider _clock = new();
    private readonly JsonWebTokenHandler _handler = new();
    private readonly JwtTokenService _service;

    public JwtTokenServiceTests()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            SigningKey = SigningKey,
            AccessTokenMinutes = AccessTokenMinutes,
        });

        _service = new JwtTokenService(options, _clock);
    }

    [Fact]
    public void CreateAccessToken_lleva_los_claims_de_la_sesion()
    {
        var token = Read(_service.CreateAccessToken(UserId, UserRole.Patient, SessionId));

        token.GetClaim(AppClaims.UserId).Value.ShouldBe("7");
        token.GetClaim(AppClaims.Role).Value.ShouldBe("Patient");
        token.GetClaim(AppClaims.SessionId).Value.ShouldBe("42");
        token.GetClaim(AppClaims.TokenId).Value.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Administrator)]
    public void CreateAccessToken_lleva_el_nombre_del_rol(UserRole role)
    {
        var token = Read(_service.CreateAccessToken(UserId, role, SessionId));

        token.GetClaim(AppClaims.Role).Value.ShouldBe(role.ToString());
    }

    [Fact]
    public void CreateAccessToken_vence_segun_la_configuracion()
    {
        var expectedExpiration = _clock.UtcNow.AddMinutes(AccessTokenMinutes);

        var accessToken = _service.CreateAccessToken(UserId, UserRole.Patient, SessionId);
        var token = Read(accessToken);

        accessToken.ExpiresAt.ShouldBe(expectedExpiration);
        token.IssuedAt.ShouldBe(_clock.UtcNow.UtcDateTime);
        token.ValidTo.ShouldBe(expectedExpiration.UtcDateTime);
    }

    [Fact]
    public void CreateAccessToken_lleva_emisor_y_audiencia()
    {
        var token = Read(_service.CreateAccessToken(UserId, UserRole.Patient, SessionId));

        token.Issuer.ShouldBe(Issuer);
        token.Audiences.ShouldBe([Audience]);
    }

    [Fact]
    public async Task CreateAccessToken_firma_con_HS256_y_la_llave_configurada()
    {
        var accessToken = _service.CreateAccessToken(UserId, UserRole.Patient, SessionId);

        var result = await _handler.ValidateTokenAsync(accessToken.Value, ValidationParameters(SigningKey));

        Read(accessToken).Alg.ShouldBe(ExpectedAlgorithm);
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAccessToken_con_otra_llave_no_valida()
    {
        var accessToken = _service.CreateAccessToken(UserId, UserRole.Patient, SessionId);

        var result = await _handler.ValidateTokenAsync(accessToken.Value, ValidationParameters(OtherSigningKey));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void CreateAccessToken_emite_un_jti_distinto_en_cada_token()
    {
        var first = Read(_service.CreateAccessToken(UserId, UserRole.Patient, SessionId));
        var second = Read(_service.CreateAccessToken(UserId, UserRole.Patient, SessionId));

        first.GetClaim(AppClaims.TokenId).Value.ShouldNotBe(second.GetClaim(AppClaims.TokenId).Value);
    }

    private JsonWebToken Read(AccessToken accessToken) => _handler.ReadJsonWebToken(accessToken.Value);

    // El reloj de prueba está en el pasado, por eso aquí no se valida la vigencia.
    private static TokenValidationParameters ValidationParameters(string signingKey) => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidateLifetime = false,
    };
}
