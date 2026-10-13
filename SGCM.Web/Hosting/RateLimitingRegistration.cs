using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Identity;
using SGCM.Data.Domain.Common;

namespace SGCM.Web.Hosting;

/// <summary>
/// Nombres de las políticas de rate limiting de la API.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Nombre de la política del endpoint de inicio de sesión.
    /// </summary>
    public const string Login = "login";
}

/// <summary>
/// Registra el rate limiting por IP de la API con el limitador integrado de ASP.NET Core.
/// </summary>
/// <remarks>
/// El límite por IP corta antes de llegar al comando; el límite por correo lo aplica el
/// comando con <c>IAttemptLimiter</c>.
/// </remarks>
public static class RateLimitingRegistration
{
    // Las peticiones sin IP comparten esta partición.
    private const string UnknownIpPartition = "unknown";

    private const string ProblemJsonContentType = "application/problem+json";

    /// <summary>
    /// Agrega la política <see cref="RateLimitPolicies.Login"/>, con una ventana fija por IP
    /// configurada en <see cref="LoginLimitsOptions"/>, y la respuesta 429 con ProblemDetails.
    /// </summary>
    /// <param name="services">Colección de servicios de la app.</param>
    /// <returns>La misma colección, para encadenar llamadas.</returns>
    public static IServiceCollection AddSgcmRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicies.Login, CreateLoginPartition);
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    // Detrás de un proxy, RemoteIpAddress es la del proxy hasta configurar los forwarded
    // headers; queda para el PBI de despliegue.
    private static RateLimitPartition<string> CreateLoginPartition(HttpContext httpContext)
    {
        var limits = httpContext.RequestServices.GetRequiredService<IOptions<LoginLimitsOptions>>().Value;
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownIpPartition;

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limits.MaxRequestsPerIp,
            Window = TimeSpan.FromSeconds(limits.IpWindowSeconds),
            QueueLimit = 0
        });
    }

    // Mismo formato que ApiControllerBase.ErrorResult, para que el cliente lea igual todos los errores.
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = CommonErrors.TooManyRequests.Description
        };
        problem.Extensions["code"] = CommonErrors.TooManyRequests.Code;

        await response.WriteAsJsonAsync(problem, options: null, ProblemJsonContentType, cancellationToken);
    }
}
