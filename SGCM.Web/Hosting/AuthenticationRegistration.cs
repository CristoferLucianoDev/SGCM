using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Infrastructure.Security;

namespace SGCM.Web.Hosting;

/// <summary>
/// Registra la autenticación JWT Bearer de la API, que valida los access tokens emitidos
/// por <see cref="ITokenService"/> con los mismos parámetros de <see cref="JwtOptions"/>.
/// </summary>
public static class AuthenticationRegistration
{
    /// <summary>
    /// Agrega el esquema JWT Bearer como esquema por defecto y lo configura a partir de las
    /// opciones <see cref="JwtOptions"/> ya validadas al arrancar.
    /// </summary>
    /// <param name="services">Colección de servicios de la app.</param>
    /// <returns>La misma colección, para encadenar llamadas.</returns>
    public static IServiceCollection AddSgcmAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Sin esto ASP.NET renombra sub y role a URIs largas de Microsoft; así
                // [Authorize(Roles = "Patient")] lee el claim role tal como lo emite JwtTokenService.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = CreateValidationParameters(jwt);
            });

        return services;
    }

    private static TokenValidationParameters CreateValidationParameters(JwtOptions jwt) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        // SigningKey nunca es nulo aquí: JwtOptionsValidator lo exige al arrancar.
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey!)),
        // Solo HS256, para que nadie presente un token con otro algoritmo o con alg: none.
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        // La arquitectura fija la tolerancia en 30 segundos en lugar de los 5 minutos por defecto.
        ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
        NameClaimType = AppClaims.UserId,
        RoleClaimType = AppClaims.Role,
    };
}
