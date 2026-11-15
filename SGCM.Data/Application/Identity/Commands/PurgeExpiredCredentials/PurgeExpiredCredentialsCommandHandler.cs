using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Identity.Commands.PurgeExpiredCredentials;

/// <summary>
/// Borra los tokens y las sesiones que terminaron antes de la hora actual menos el período de
/// retención. Qué se borra lo decide el repositorio, con la misma regla que
/// <see cref="UserToken.EndedBefore"/> y <see cref="UserSession.EndedBefore"/>.
/// No guarda: el borrado va directo a la base dentro de la transacción del decorador.
/// </summary>
internal sealed class PurgeExpiredCredentialsCommandHandler(
    IUserRepository userRepository,
    IDateTimeProvider dateTimeProvider,
    IOptions<AccountsOptions> accountsOptions) : ICommandHandler<PurgeExpiredCredentialsCommand, int>
{
    public async Task<Result<int>> HandleAsync(PurgeExpiredCredentialsCommand command, CancellationToken cancellationToken)
    {
        var cutoff = dateTimeProvider.UtcNow - TimeSpan.FromDays(accountsOptions.Value.CredentialRetentionDays);

        return await userRepository.DeleteEndedCredentialsAsync(cutoff, cancellationToken);
    }
}
