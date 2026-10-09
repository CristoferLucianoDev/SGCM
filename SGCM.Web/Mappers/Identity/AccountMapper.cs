using SGCM.Data.Application.Identity.Commands.RegisterPatientAccount;
using SGCM.Web.Contracts.Identity;

namespace SGCM.Web.Mappers.Identity;

public static class AccountMapper
{
    public static RegisterPatientAccountCommand ToCommand(this RegisterPatientRequest request) =>
        new(request.Email, request.Password);
}