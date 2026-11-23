using FluentValidation;
using FluentValidation.Results;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.InsuranceProviders;

namespace SGCM.Data.Application.People.Commands.UpdateInsuranceProvider;

/// <summary>
/// Valida el identificador y los campos enviados con las mismas reglas del agregado, y asocia el primer
/// error a su campo. Los campos no enviados no se validan.
/// </summary>
public sealed class UpdateInsuranceProviderCommandValidator : AbstractValidator<UpdateInsuranceProviderCommand>
{
    // Datos válidos de referencia: solo importan los campos enviados, que son los que Update valida.
    private const string ReferenceName = "Proveedor de referencia";
    private const string ReferencePhone = "8095550100";
    private const string ReferenceEmail = "contacto@referencia.com";
    private const string ReferenceAddress = "Dirección de referencia";

    /// <summary>
    /// Define las reglas de validación del comando.
    /// </summary>
    public UpdateInsuranceProviderCommandValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.Id)
            .GreaterThan(0)
            .WithErrorCode(InsuranceProviderErrors.InvalidId.Code)
            .WithMessage(InsuranceProviderErrors.InvalidId.Description);

        RuleFor(command => command).Custom((command, context) =>
        {
            var reference = InsuranceProvider.Create(
                ReferenceName, ReferencePhone, ReferenceEmail, ReferenceAddress, website: null).Value;
            var result = reference.Update(command.Changes);
            if (result.IsSuccess)
            {
                return;
            }

            context.AddFailure(new ValidationFailure(PropertyOf(result.Error), result.Error.Description)
            {
                ErrorCode = result.Error.Code
            });
        });
    }

    private static string PropertyOf(Error error) => error switch
    {
        _ when error == InsuranceProviderErrors.NoFieldsProvided => string.Empty,
        _ when error == InsuranceProviderErrors.NameRequired || error == InsuranceProviderErrors.NameTooLong
            => nameof(InsuranceProviderChanges.Name),
        _ when error == InsuranceProviderErrors.ContactPhoneRequired || error == InsuranceProviderErrors.ContactPhoneTooLong
            => nameof(InsuranceProviderChanges.ContactPhone),
        // El correo inválido viene de Email.Create; como el proveedor tiene un solo correo, su
        // mensaje ya identifica el campo.
        _ when error == CommonErrors.InvalidEmail => nameof(InsuranceProviderChanges.ContactEmail),
        _ when error == InsuranceProviderErrors.AddressRequired || error == InsuranceProviderErrors.AddressTooLong
            => nameof(InsuranceProviderChanges.Address),
        _ when error == InsuranceProviderErrors.WebsiteTooLong || error == InsuranceProviderErrors.WebsiteInvalid
            => nameof(InsuranceProviderChanges.Website),
        _ => throw new InvalidOperationException(
            $"El error {error.Code} no tiene un campo asociado en la actualización del proveedor.")
    };
}
