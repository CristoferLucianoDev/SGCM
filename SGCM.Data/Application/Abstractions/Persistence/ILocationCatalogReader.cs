using SGCM.Data.Application.People.Queries.ListMunicipalities;
using SGCM.Data.Application.People.Queries.ListProvinces;

namespace SGCM.Data.Application.Abstractions.Persistence;

internal interface ILocationCatalogReader
{
    Task<IReadOnlyList<ProvinceDto>> ListProvincesAsync(CancellationToken cancellationToken);

    /// <returns><see langword="null"/> si la provincia no existe. Una lista vacía si existe y no tiene municipios.</returns>
    Task<IReadOnlyList<MunicipalityDto>?> ListMunicipalitiesAsync(int provinceId, CancellationToken cancellationToken);
}
