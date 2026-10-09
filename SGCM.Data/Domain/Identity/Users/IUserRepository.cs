using SGCM.Data.Domain.Common.Interfaces;
using SGCM.Data.Domain.Common.ValueObjects;

namespace SGCM.Data.Domain.Identity.Users;

public interface IUserRepository : IRepository<User>
{
    // Trae el usuario con sus tokens, porque el agregado los necesita para emitir o consumir uno.
    Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken);
}