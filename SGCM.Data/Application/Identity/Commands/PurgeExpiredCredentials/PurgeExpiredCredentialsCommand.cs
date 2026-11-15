using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Identity.Commands.PurgeExpiredCredentials;

/// <summary>
/// Borrado de los tokens y las sesiones que terminaron hace más del período de retención.
/// Lo ejecuta un proceso automático y devuelve la cantidad de tokens y sesiones borrados.
/// </summary>
public sealed record PurgeExpiredCredentialsCommand : ICommand<int>;
