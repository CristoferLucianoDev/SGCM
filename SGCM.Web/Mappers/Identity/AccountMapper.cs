using SGCM.Data.Application.Identity.Commands.ConfirmEmail;
using SGCM.Data.Application.Identity.Commands.RegisterPatientAccount;
using SGCM.Data.Application.Identity.Commands.ResendConfirmationEmail;
using SGCM.Web.Contracts.Identity;

namespace SGCM.Web.Mappers.Identity;

public static class AccountMapper
{
    public static RegisterPatientAccountCommand ToCommand(this RegisterPatientRequest request) =>
        new(request.Email, request.Password);

    public static ConfirmEmailCommand ToCommand(this ConfirmEmailRequest request) => new(request.Token);

    public static ResendConfirmationEmailCommand ToCommand(this ResendConfirmationEmailRequest request) => new(request.Email);
}