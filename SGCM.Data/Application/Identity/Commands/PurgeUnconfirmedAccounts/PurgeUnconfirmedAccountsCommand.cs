using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Identity.Commands.PurgeUnconfirmedAccounts;

/// <summary>
/// Borrado de los registros pendientes cuyo último token de confirmación venció.
/// Lo ejecuta un proceso automático y devuelve la cantidad de cuentas borradas.
/// </summary>
public sealed record PurgeUnconfirmedAccountsCommand : ICommand<int>;
