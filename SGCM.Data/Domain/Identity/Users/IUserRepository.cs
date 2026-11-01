using SGCM.Data.Domain.Common.Interfaces;
using SGCM.Data.Domain.Common.ValueObjects;

namespace SGCM.Data.Domain.Identity.Users;

public interface IUserRepository : IRepository<User>
{
    // Trae el usuario con sus tokens, porque el agregado los necesita para emitir o consumir uno.
    Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken);

    /// <summary>
    /// Igual que <see cref="GetByEmailAsync"/>, pero antes bloquea la fila del usuario hasta
    /// el fin de la transacción, para que la limpieza de registros vencidos y otros reenvíos
    /// no la modifiquen mientras se usa.
    /// </summary>
    /// <param name="email">Correo del usuario.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>El usuario con sus tokens, o <c>null</c> si no existe.</returns>
    /// <exception cref="InvalidOperationException">Si no hay una transacción activa.</exception>
    Task<User?> GetByEmailForUpdateAsync(Email email, CancellationToken cancellationToken);

    /// <summary>
    /// Busca el usuario por su id, con sus tokens y sesiones, y antes bloquea su fila hasta el
    /// fin de la transacción. Lo usan los comandos que parten del usuario autenticado y abren
    /// una sesión nueva, por la misma razón que <see cref="GetByEmailForUpdateAsync"/>.
    /// </summary>
    /// <param name="userId">Id del usuario.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>El usuario con sus tokens y sesiones, o <c>null</c> si no existe.</returns>
    /// <exception cref="InvalidOperationException">Si no hay una transacción activa.</exception>
    Task<User?> GetByIdForUpdateAsync(int userId, CancellationToken cancellationToken);

    // Busca, con sus tokens, el usuario que tiene un token de ese tipo con ese hash; devuelve null si no existe.
    Task<User?> GetByTokenHashAsync(UserTokenType type, string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Igual que <see cref="GetByTokenHashAsync"/>, pero antes bloquea la fila del usuario hasta
    /// el fin de la transacción, para que dos usos simultáneos del mismo token no lo consuman
    /// los dos.
    /// </summary>
    /// <param name="type">Propósito del token.</param>
    /// <param name="tokenHash">Hash del token recibido.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>El usuario con sus tokens y sesiones, o <c>null</c> si ningún token coincide.</returns>
    /// <exception cref="InvalidOperationException">Si no hay una transacción activa.</exception>
    Task<User?> GetByTokenHashForUpdateAsync(UserTokenType type, string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Borra las cuentas sin confirmar que no tienen ningún token de confirmación vigente
    /// en <paramref name="now"/>. Salta los usuarios cuya fila está bloqueada por otra
    /// transacción; la siguiente pasada los vuelve a evaluar.
    /// </summary>
    /// <param name="now">Momento contra el que se evalúa la vigencia de los tokens.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>La cantidad de cuentas borradas.</returns>
    /// <exception cref="InvalidOperationException">Si no hay una transacción activa.</exception>
    Task<int> DeleteExpiredPendingRegistrationsAsync(DateTimeOffset now, CancellationToken cancellationToken);
}