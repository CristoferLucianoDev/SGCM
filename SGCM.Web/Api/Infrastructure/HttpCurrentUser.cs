using System.Security.Claims;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Web.Api.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identities.Any(identity => identity.IsAuthenticated) == true;

    public bool IsInRole(UserRole role) => accessor.HttpContext?.User.Identities.Any(identity =>
        identity.IsAuthenticated && identity.HasClaim(identity.RoleClaimType, role.ToString())) == true;

    public int UserId =>
        int.TryParse(AuthenticatedIdentity?.FindFirst(AppClaims.UserId)?.Value, out var userId)
            ? userId
            : throw new InvalidOperationException("La petición no tiene un usuario autenticado.");

    private ClaimsIdentity? AuthenticatedIdentity =>
        accessor.HttpContext?.User.Identities.FirstOrDefault(identity => identity.IsAuthenticated);
}
