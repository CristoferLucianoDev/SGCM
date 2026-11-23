using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.InsuranceProviders;

namespace SGCM.Data.Application.People.Commands.UpdateInsuranceProvider;

/// <summary>
/// Actualiza solo los campos enviados de un proveedor de seguros. El guardado lo hace el decorador de
/// transacción, por eso el handler no llama a la unidad de trabajo.
/// </summary>
internal sealed class UpdateInsuranceProviderCommandHandler(IInsuranceProviderRepository repository)
    : ICommandHandler<UpdateInsuranceProviderCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateInsuranceProviderCommand command, CancellationToken cancellationToken)
    {
        var provider = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (provider is null)
        {
            return InsuranceProviderErrors.NotFound;
        }

        var candidate = ValidateOnCopy(provider, command.Changes);
        if (candidate.IsFailure)
        {
            return candidate.Error;
        }

        if (command.Changes.Name.IsProvided
            && await repository.ExistsByNameAsync(candidate.Value.Name, command.Id, cancellationToken))
        {
            return InsuranceProviderErrors.NameAlreadyInUse;
        }

        var result = provider.Update(command.Changes);
        if (result.IsFailure)
        {
            return result.Error;
        }

        return Unit.Value;
    }

    // Valida sobre una copia para no alterar la entidad rastreada antes de comprobar la unicidad; la
    // copia expone el nombre ya recortado, que es el que se compara.
    private static Result<InsuranceProvider> ValidateOnCopy(InsuranceProvider provider, InsuranceProviderChanges changes)
    {
        var copy = InsuranceProvider.Create(
            provider.Name, provider.ContactPhone, provider.ContactEmail.Value, provider.Address, provider.Website);
        if (copy.IsFailure)
        {
            return copy.Error;
        }

        var validation = copy.Value.Update(changes);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return copy;
    }
}
