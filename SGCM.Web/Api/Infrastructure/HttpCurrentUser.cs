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

    public bool TryGetSessionIdentity(out int userId, out int sessionId)
    {
        userId = sessionId = 0;
        var identities = accessor.HttpContext?.User.Identities.Where(identity => identity.IsAuthenticated).ToArray();
        if (identities is not { Length: 1 }) return false;

        var subjects = identities[0].FindAll(AppClaims.UserId).ToArray();
        var sessions = identities[0].FindAll(AppClaims.SessionId).ToArray();
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
