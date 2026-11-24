using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.People.Queries.SearchDoctors;

internal sealed class SearchDoctorsQueryHandler(IDoctorSearchReader reader, IValidator<SearchDoctorsQuery> validator)
    : IQueryHandler<SearchDoctorsQuery, PagedResult<DoctorSearchDto>>
{
    public async Task<Result<PagedResult<DoctorSearchDto>>> HandleAsync(
        SearchDoctorsQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var criteria = new DoctorSearchCriteria(search, query.PageNumber, query.PageSize,
            SpecialtyId: query.SpecialtyId, ProvinceId: query.ProvinceId, MunicipalityId: query.MunicipalityId);
        return await reader.SearchAsync(criteria, cancellationToken);
    }
}
