using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.SearchDoctors;

namespace SGCM.Data.Application.Abstractions.Persistence;

internal interface IDoctorSearchReader
{
    Task<PagedResult<DoctorSearchDto>> SearchAsync(
        string? search, int pageNumber, int pageSize, CancellationToken cancellationToken);
}
