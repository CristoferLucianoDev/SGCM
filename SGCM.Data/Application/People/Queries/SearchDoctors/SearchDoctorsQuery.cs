using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Application.People.Queries.SearchDoctors;

/// <summary>Busca médicos del directorio por nombre, especialidad y ubicación.</summary>
public sealed record SearchDoctorsQuery(
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 20,
    int? SpecialtyId = null,
    int? ProvinceId = null,
    int? MunicipalityId = null)
    : IQuery<PagedResult<DoctorSearchDto>>;
