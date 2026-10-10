using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Identity;

internal sealed class UserRepository(AppDbContext context) : IUserRepository
{
    public Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken) =>
        context.Users
            .Include(user => user.Tokens)
            .SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

    public Task<User?> GetByTokenHashAsync(UserTokenType type, string tokenHash, CancellationToken cancellationToken) =>
        context.Users
            .Include(user => user.Tokens)
            .SingleOrDefaultAsync(
                user => user.Tokens.Any(token => token.Type == type && token.TokenHash == tokenHash),
                cancellationToken);

    public void Add(User user) => context.Users.Add(user);
}