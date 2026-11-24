using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.SearchDoctors;

namespace SGCM.Data.Application.Abstractions.Persistence;

internal interface IDoctorSearchReader
{
    Task<PagedResult<DoctorSearchDto>> SearchAsync(DoctorSearchCriteria criteria, CancellationToken cancellationToken);
}

internal sealed record DoctorSearchCriteria(
    string? Search, int PageNumber, int PageSize, int? SpecialtyId, int? ProvinceId, int? MunicipalityId);
