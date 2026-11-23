using SGCM.Data.Application.People.Queries.ListMunicipalities;
using SGCM.Data.Application.People.Queries.ListProvinces;
using SGCM.Web.Contracts.People;

namespace SGCM.Web.Mappers.People;

internal static class LocationMapper
{
    public static ProvinceResponse ToResponse(this ProvinceDto province) => new(province.Id, province.Name);

    public static MunicipalityResponse ToResponse(this MunicipalityDto municipality) => new(municipality.Id, municipality.Name);
}
