using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.ListSpecialties;

namespace SGCM.Data.Application.Abstractions.Persistence;

public interface ISpecialtyListReader
{
    Task<PagedResult<SpecialtyDto>> ListAsync(
        string? search, bool? isActive, bool includeStatus, int pageNumber, int pageSize,
        CancellationToken cancellationToken);
}
