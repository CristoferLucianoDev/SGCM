using SGCM.Data.Application.People.Commands.CreateSpecialty;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.ListSpecialties;
using SGCM.Web.Contracts.People;

namespace SGCM.Web.Mappers.People;

public static class SpecialtyMapper
{
    public static CreateSpecialtyCommand ToCommand(this CreateSpecialtyRequest request) =>
        new(request.Name, request.Description);

    public static ListSpecialtiesQuery ToQuery(this ListSpecialtiesRequest request) =>
        new(request.Search, request.Status switch
        {
            SpecialtyStatusFilter.Active => true,
            SpecialtyStatusFilter.Inactive => false,
            SpecialtyStatusFilter.All => (bool?)null,
            _ => throw new ArgumentOutOfRangeException(nameof(request.Status), request.Status, "El estado no es válido.")
        }, request.PageNumber, request.PageSize);

    public static SpecialtyResponse ToResponse(this SpecialtyDto specialty) =>
        new(specialty.Id, specialty.Name, specialty.Description, specialty.IsActive);

    public static ListSpecialtiesResponse ToResponse(this PagedResult<SpecialtyDto> page) =>
        new(page.Items.Select(specialty => specialty.ToResponse()).ToArray(),
            page.PageNumber, page.PageSize, page.TotalCount,
            page.TotalCount == 0 ? "No existen especialidades disponibles" : null);
}
