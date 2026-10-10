using SGCM.Data.Domain.Common.Interfaces;
using SGCM.Data.Domain.Common.ValueObjects;

namespace SGCM.Data.Domain.Identity.Users;

public interface IUserRepository : IRepository<User>
{
    // Trae el usuario con sus tokens, porque el agregado los necesita para emitir o consumir uno.
    Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken);

    // Busca, con sus tokens, el usuario que tiene un token de ese tipo con ese hash; devuelve null si no existe.
    Task<User?> GetByTokenHashAsync(UserTokenType type, string tokenHash, CancellationToken cancellationToken);
}