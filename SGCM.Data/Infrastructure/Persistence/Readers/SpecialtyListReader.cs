using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.ListSpecialties;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

internal sealed class SpecialtyListReader(AppDbContext context) : ISpecialtyListReader
{
    public async Task<PagedResult<SpecialtyDto>> ListAsync(
        string? search, bool? isActive, bool includeStatus, int pageNumber, int pageSize,
        CancellationToken cancellationToken)
    {
        var query = context.Specialties.AsNoTracking();
        if (isActive.HasValue)
        {
            query = query.Where(specialty => specialty.IsActive == isActive.Value);
        }

        if (search is not null)
        {
            query = query.Where(specialty => PostgresFunctions.NormalizeText(specialty.Name)!
                .Contains(PostgresFunctions.NormalizeText(search)!));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var offset = checked((int)(((long)pageNumber - 1) * pageSize));
        var items = await query.OrderBy(specialty => specialty.Name).ThenBy(specialty => specialty.Id)
            .Skip(offset).Take(pageSize)
            .Select(specialty => new SpecialtyDto(specialty.Id, specialty.Name, specialty.Description,
                includeStatus ? specialty.IsActive : (bool?)null))
            .ToListAsync(cancellationToken);

        return new PagedResult<SpecialtyDto>(items, pageNumber, pageSize, totalCount);
    }
}
