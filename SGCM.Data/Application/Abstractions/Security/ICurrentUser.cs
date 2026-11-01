using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Abstractions.Security;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    bool IsInRole(UserRole role);
}
