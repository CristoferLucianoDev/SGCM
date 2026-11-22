using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Application.People.Queries.SearchDoctors;

/// <summary>Busca por un fragmento del nombre completo de los médicos visibles en el directorio.</summary>
public sealed record SearchDoctorsQuery(string? Search = null, int PageNumber = 1, int PageSize = 20)
    : IQuery<PagedResult<DoctorSearchDto>>;
