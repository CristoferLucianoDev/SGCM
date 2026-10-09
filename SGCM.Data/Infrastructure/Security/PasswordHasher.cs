using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Identity.Users;
using IdentityPasswordHasher = Microsoft.AspNetCore.Identity.PasswordHasher<object>;

namespace SGCM.Data.Infrastructure.Security;

/// <summary>
/// PBKDF2 del PasswordHasher de ASP.NET Core Identity (formato V3), usado sin el resto de Identity.
/// </summary>
internal sealed class PasswordHasher : IPasswordHasher
{
    // La implementación de Identity no usa el usuario para calcular el hash.
    private static readonly object NoUser = new();

    private readonly IdentityPasswordHasher _inner = new();

    public string Hash(Password password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return _inner.HashPassword(NoUser, password.Value);
    }
}