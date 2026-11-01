using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public class PendingStepFilterTests
{
    private readonly PendingStepFilter _filter = new();

    [Fact]
    public void Endpoint_protegido_sin_el_atributo_responde_403_al_usuario_con_paso_pendiente()
    {
        var context = CreateContext(PendingStep.PatientProfile, new AuthorizeAttribute());

        _filter.OnAuthorization(context);

        context.Result.ShouldBeOfType<ForbidResult>();
    }

    [Fact]
    public void Endpoint_que_resuelve_el_paso_deja_pasar_al_usuario()
    {
        var context = CreateContext(
            PendingStep.PatientProfile, new AuthorizeAttribute(), new AllowPendingStepAttribute(PendingStep.PatientProfile));

        _filter.OnAuthorization(context);

        context.Result.ShouldBeNull();
    }

    [Fact]
    public void Endpoint_que_resuelve_otro_paso_responde_403()
    {
        var context = CreateContext(
            PendingStep.ChangeTemporaryPassword, new AuthorizeAttribute(), new AllowPendingStepAttribute(PendingStep.PatientProfile));

        _filter.OnAuthorization(context);

        context.Result.ShouldBeOfType<ForbidResult>();
    }

    [Fact]
    public void Usuario_sin_paso_pendiente_pasa_aunque_el_endpoint_no_tenga_el_atributo()
    {
        var context = CreateContext(pendingStep: null, new AuthorizeAttribute());

        _filter.OnAuthorization(context);

        context.Result.ShouldBeNull();
    }

    [Fact]
    public void Endpoint_sin_autorizacion_no_se_revisa()
    {
        var context = CreateContext(PendingStep.PatientProfile);

        _filter.OnAuthorization(context);

        context.Result.ShouldBeNull();
    }

    [Fact]
    public void Endpoint_anonimo_no_se_revisa()
    {
        var context = CreateContext(PendingStep.PatientProfile, new AuthorizeAttribute(), new AllowAnonymousAttribute());

        _filter.OnAuthorization(context);

        context.Result.ShouldBeNull();
    }

    private static AuthorizationFilterContext CreateContext(PendingStep? pendingStep, params object[] endpointMetadata)
    {
        var claims = new List<Claim> { new(AppClaims.UserId, "7") };
        if (pendingStep is { } step)
        {
            claims.Add(new Claim(AppClaims.PendingStep, PendingStepClaimValues.From(step)));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"))
        };
        var action = new ActionDescriptor { EndpointMetadata = endpointMetadata.ToList() };

        return new AuthorizationFilterContext(new ActionContext(httpContext, new RouteData(), action), []);
    }
}
