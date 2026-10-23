using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.People;

internal sealed class SpecialtyRepository(AppDbContext context) : ISpecialtyRepository
{
    public Task<bool> ExistsByNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default) =>
        context.Specialties.AnyAsync(specialty =>
            (!excludeId.HasValue || specialty.Id != excludeId.Value)
            && PostgresFunctions.NormalizeText(specialty.Name) == PostgresFunctions.NormalizeText(name),
            cancellationToken);

    public void Add(Specialty specialty) => context.Specialties.Add(specialty);
}
