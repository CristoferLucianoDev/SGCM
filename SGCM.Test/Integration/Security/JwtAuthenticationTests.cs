using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;

namespace SGCM.Test.Integration.Security;

/// <summary>
/// Pruebas de que la API valida los access tokens de la app con la configuración real del
/// esquema JWT Bearer.
/// </summary>
[Trait("Category", "Integration")]
public sealed class JwtAuthenticationTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const int UserId = 7;
    private const int SessionId = 42;
    private const int AccessTokenMinutes = 15;

    // Tolerancia de reloj que fija la arquitectura.
    private static readonly TimeSpan ExpectedClockSkew = TimeSpan.FromSeconds(30);

    // Un minuto más que la vigencia: el token venció hace un minuto, más que la tolerancia.
    private static readonly TimeSpan ExpiredIssueOffset = TimeSpan.FromMinutes(AccessTokenMinutes + 1);

    private static readonly string OtherSigningKey = new('x', JwtOptions.MinimumSigningKeyBytes);

    private readonly JsonWebTokenHandler _handler = new();

    private JwtBearerOptions Bearer => Factory.Services
        .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
        .Get(JwtBearerDefaults.AuthenticationScheme);

    private JwtOptions Jwt => Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

    private ITokenService TokenService => Factory.Services.GetRequiredService<ITokenService>();

    [Fact]
    public void La_api_valida_tokens_con_los_parametros_de_la_configuracion()
    {
        var bearer = Bearer;
        var parameters = bearer.TokenValidationParameters;

        bearer.MapInboundClaims.ShouldBeFalse();
        parameters.ValidIssuer.ShouldBe(Jwt.Issuer);
        parameters.ValidAudience.ShouldBe(Jwt.Audience);
        parameters.ValidAlgorithms.ShouldBe([SecurityAlgorithms.HmacSha256]);
        parameters.ClockSkew.ShouldBe(ExpectedClockSkew);
        parameters.NameClaimType.ShouldBe(AppClaims.UserId);
        parameters.RoleClaimType.ShouldBe(AppClaims.Role);
    }

    [Fact]
    public async Task Un_token_emitido_por_la_app_es_valido()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var accessToken = TokenService.CreateAccessToken(UserId, UserRole.Patient, SessionId);

        var result = await ValidateAsync(accessToken.Value);

        result.IsValid.ShouldBeTrue();
        var user = new ClaimsPrincipal(result.ClaimsIdentity);
        user.Identity!.Name.ShouldBe("7");
        user.IsInRole(nameof(UserRole.Patient)).ShouldBeTrue();
        user.IsInRole(nameof(UserRole.Administrator)).ShouldBeFalse();
    }

    [Fact]
    public async Task Un_token_vencido_se_rechaza()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow - ExpiredIssueOffset;
        var accessToken = TokenService.CreateAccessToken(UserId, UserRole.Patient, SessionId);

        var result = await ValidateAsync(accessToken.Value);

        result.IsValid.ShouldBeFalse();
        result.Exception.ShouldBeOfType<SecurityTokenExpiredException>();
    }

    [Fact]
    public async Task Un_token_firmado_con_otra_llave_se_rechaza()
    {
        var now = DateTime.UtcNow;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Jwt.Issuer,
            Audience = Jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(OtherSigningKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [AppClaims.UserId] = "7",
                [AppClaims.Role] = nameof(UserRole.Patient),
            },
        });

        var result = await ValidateAsync(token);

        result.IsValid.ShouldBeFalse();
    }

    private Task<TokenValidationResult> ValidateAsync(string token) =>
        _handler.ValidateTokenAsync(token, Bearer.TokenValidationParameters);
}
