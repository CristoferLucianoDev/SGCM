using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.People.Queries.ListMunicipalities;

public sealed record ListMunicipalitiesQuery(int ProvinceId) : IQuery<IReadOnlyList<MunicipalityDto>>;
