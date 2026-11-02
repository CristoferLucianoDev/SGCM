using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SGCM.Data.Application.Abstractions.Security;

namespace SGCM.Web.Api.Infrastructure;

/// <summary>
/// Responde 403 si el usuario tiene un paso pendiente (claim <see cref="AppClaims.PendingStep"/>)
/// y el endpoint no lo resuelve con <see cref="AllowPendingStepAttribute"/>. Solo revisa los
/// endpoints que exigen autenticación; la autenticación y los roles los resuelve antes
/// <c>[Authorize]</c>.
/// </summary>
public sealed class PendingStepFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;

        if (!metadata.OfType<IAuthorizeData>().Any() || metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        var pendingStep = context.HttpContext.User.FindFirst(AppClaims.PendingStep)?.Value;

        if (pendingStep is null)
        {
            return;
        }

        var allowed = metadata.OfType<AllowPendingStepAttribute>()
            .Any(attribute => PendingStepClaimValues.From(attribute.Step) == pendingStep);

        if (!allowed)
        {
            context.Result = new ForbidResult();
        }
    }
}
