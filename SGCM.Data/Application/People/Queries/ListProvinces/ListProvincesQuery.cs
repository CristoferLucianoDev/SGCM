using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.People.Queries.ListProvinces;

public sealed record ListProvincesQuery() : IQuery<IReadOnlyList<ProvinceDto>>;
