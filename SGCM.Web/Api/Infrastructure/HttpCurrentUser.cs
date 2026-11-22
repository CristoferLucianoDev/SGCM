using System.Globalization;
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

    public bool TryGetSessionIdentity(out int userId, out int sessionId) =>
        TryGetSessionIdentityCore(null, out userId, out sessionId);

    public bool TryGetSessionIdentity(UserRole requiredRole, out int userId, out int sessionId) =>
        TryGetSessionIdentityCore(requiredRole, out userId, out sessionId);

    private bool TryGetSessionIdentityCore(UserRole? requiredRole, out int userId, out int sessionId)
    {
        userId = sessionId = 0;
        var identities = accessor.HttpContext?.User.Identities.Where(identity => identity.IsAuthenticated).ToArray();
        if (identities is not { Length: 1 }) return false;

        var identity = identities[0];
        if (requiredRole is { } role)
        {
            var roles = identity.FindAll(identity.RoleClaimType).ToArray();
            if (roles.Length != 1 || roles[0].Value != role.ToString()) return false;
        }

        var subjects = identity.FindAll(AppClaims.UserId).ToArray();
        var sessions = identity.FindAll(AppClaims.SessionId).ToArray();
        if (subjects.Length != 1 || sessions.Length != 1
            || !int.TryParse(subjects[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedUser)
            || !int.TryParse(sessions[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSession)
            || parsedUser <= 0 || parsedSession <= 0) return false;

        userId = parsedUser;
        sessionId = parsedSession;
        return true;
    }

    private ClaimsIdentity? AuthenticatedIdentity =>
        accessor.HttpContext?.User.Identities.FirstOrDefault(identity => identity.IsAuthenticated);
}
