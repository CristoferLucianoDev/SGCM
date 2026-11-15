using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.InsuranceProviders;

namespace SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;

/// <summary>
/// Valida el registro con las mismas reglas del agregado y asocia el primer error a su campo.
/// </summary>
public sealed class RegisterInsuranceProviderCommandValidator : AbstractValidator<RegisterInsuranceProviderCommand>
{
    public RegisterInsuranceProviderCommandValidator()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            var result = InsuranceProvider.Create(
                command.Name, command.ContactPhone, command.ContactEmail, command.Address, command.Website);
            if (result.IsSuccess)
            {
                return;
            }

            var error = result.Error;
            context.AddFailure(new ValidationFailure(PropertyOf(error), error.Description)
            {
                ErrorCode = error.Code
            });
        });
    }

    private static string PropertyOf(Error error) => error switch
    {
        _ when error == InsuranceProviderErrors.NameRequired || error == InsuranceProviderErrors.NameTooLong
            => nameof(RegisterInsuranceProviderCommand.Name),
        _ when error == InsuranceProviderErrors.ContactPhoneRequired || error == InsuranceProviderErrors.ContactPhoneTooLong
            => nameof(RegisterInsuranceProviderCommand.ContactPhone),
        // El correo inválido viene de Email.Create; como el proveedor tiene un solo correo, su
        // mensaje ya identifica el campo.
        _ when error == CommonErrors.InvalidEmail
            => nameof(RegisterInsuranceProviderCommand.ContactEmail),
        _ when error == InsuranceProviderErrors.AddressRequired || error == InsuranceProviderErrors.AddressTooLong
            => nameof(RegisterInsuranceProviderCommand.Address),
        _ when error == InsuranceProviderErrors.WebsiteTooLong || error == InsuranceProviderErrors.WebsiteInvalid
            => nameof(RegisterInsuranceProviderCommand.Website),
        _ => throw new InvalidOperationException($"El error {error.Code} no tiene un campo asociado en el registro del proveedor.")
    };
}
