using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Application.People.Queries.ListSpecialties;

// Omitir IsActive selecciona activas; null solicita explícitamente todos los estados.
public sealed record ListSpecialtiesQuery(
    string? Search = null, bool? IsActive = true, int PageNumber = 1, int PageSize = 20)
    : IQuery<PagedResult<SpecialtyDto>>;
