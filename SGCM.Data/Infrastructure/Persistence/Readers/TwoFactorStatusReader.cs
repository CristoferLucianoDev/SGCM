using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="ITwoFactorStatusReader"/> con una consulta directa a
/// <c>app.user_two_factor</c>.
/// </summary>
internal sealed class TwoFactorStatusReader(AppDbContext context) : ITwoFactorStatusReader
{
    // Consulta directa porque la entidad del segundo factor aún no existe; la tarea
    // "Implementar las reglas de dos factores en el agregado" puede reemplazarla.
    public Task<bool> IsEnabledAsync(int userId, CancellationToken cancellationToken) =>
        context.Database
            .SqlQuery<bool>(
                $"SELECT EXISTS (SELECT 1 FROM app.user_two_factor WHERE user_id = {userId} AND is_enabled) AS \"Value\"")
            .SingleAsync(cancellationToken);
}
