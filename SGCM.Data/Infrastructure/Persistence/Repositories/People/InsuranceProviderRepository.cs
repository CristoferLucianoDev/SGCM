using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.People.InsuranceProviders;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.People;

internal sealed class InsuranceProviderRepository(AppDbContext context) : IInsuranceProviderRepository
{
    public Task<InsuranceProvider?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        context.InsuranceProviders.AsTracking().SingleOrDefaultAsync(provider => provider.Id == id, cancellationToken);

    // La comparación es exacta para coincidir con uq_insurance_provider_name; especialidades
    // normaliza porque su índice usa sp_text_normalize.
    public Task<bool> ExistsByNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();

        return context.InsuranceProviders.AnyAsync(provider =>
            (!excludeId.HasValue || provider.Id != excludeId.Value)
            && provider.Name == trimmed,
            cancellationToken);
    }

    public void Add(InsuranceProvider provider) => context.InsuranceProviders.Add(provider);
}
