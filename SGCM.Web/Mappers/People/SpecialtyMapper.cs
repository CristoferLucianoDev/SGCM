using SGCM.Data.Application.People.Commands.CreateSpecialty;
using SGCM.Web.Contracts.People;

namespace SGCM.Web.Mappers.People;

public static class SpecialtyMapper
{
    public static CreateSpecialtyCommand ToCommand(this CreateSpecialtyRequest request) =>
        new(request.Name, request.Description);
}
