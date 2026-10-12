using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.PurgeUnconfirmedAccounts;

/// <summary>
/// Borra las cuentas pendientes vencidas a la hora actual. Qué cuenta se borra lo decide
/// el repositorio, con la misma regla que <see cref="User.IsPendingRegistrationExpiredAt"/>.
/// No guarda: el borrado va directo a la base dentro de la transacción del decorador.
/// </summary>
internal sealed class PurgeUnconfirmedAccountsCommandHandler(
    IUserRepository userRepository,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<PurgeUnconfirmedAccountsCommand, int>
{
    public async Task<Result<int>> HandleAsync(PurgeUnconfirmedAccountsCommand command, CancellationToken cancellationToken) =>
        await userRepository.DeleteExpiredPendingRegistrationsAsync(dateTimeProvider.UtcNow, cancellationToken);
}
