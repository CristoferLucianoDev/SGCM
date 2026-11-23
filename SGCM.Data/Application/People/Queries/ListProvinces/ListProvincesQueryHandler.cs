using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.People.Queries.ListProvinces;

internal sealed class ListProvincesQueryHandler(ILocationCatalogReader reader)
    : IQueryHandler<ListProvincesQuery, IReadOnlyList<ProvinceDto>>
{
    public async Task<Result<IReadOnlyList<ProvinceDto>>> HandleAsync(
        ListProvincesQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provinces = await reader.ListProvincesAsync(cancellationToken);
        return Result<IReadOnlyList<ProvinceDto>>.Success(provinces);
    }
}
