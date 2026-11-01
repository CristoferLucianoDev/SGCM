using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Web.Api.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identities.Any(identity => identity.IsAuthenticated) == true;

    public bool IsInRole(UserRole role) => accessor.HttpContext?.User.Identities.Any(identity =>
        identity.IsAuthenticated && identity.HasClaim(identity.RoleClaimType, role.ToString())) == true;
}
