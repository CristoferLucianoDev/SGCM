using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IInsuranceProviderReader"/> sobre el mapeo de
/// <c>people.insurance_provider</c>.
/// </summary>
internal sealed class InsuranceProviderReader(AppDbContext context) : IInsuranceProviderReader
{
    public Task<bool> IsActiveAsync(int insuranceProviderId, CancellationToken cancellationToken) =>
        context.InsuranceProviders.AnyAsync(
            provider => provider.Id == insuranceProviderId && provider.IsActive,
            cancellationToken);
}
