using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IInsuranceProviderReader"/> con una consulta directa a
/// <c>people.insurance_provider</c>.
/// </summary>
internal sealed class InsuranceProviderReader(AppDbContext context) : IInsuranceProviderReader
{
    // Consulta directa porque InsuranceProvider aún no está mapeado en EF Core; cuando lo esté,
    // puede pasar a su repositorio.
    public Task<bool> IsActiveAsync(int insuranceProviderId, CancellationToken cancellationToken) =>
        context.Database.SqlQuery<bool>(
                $"SELECT EXISTS (SELECT 1 FROM people.insurance_provider WHERE id = {insuranceProviderId} AND is_active) AS \"Value\"")
            .SingleAsync(cancellationToken);
}
