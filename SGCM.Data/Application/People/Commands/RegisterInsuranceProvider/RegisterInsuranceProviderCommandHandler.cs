using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.InsuranceProviders;

namespace SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;

internal sealed class RegisterInsuranceProviderCommandHandler(IInsuranceProviderRepository repository, IUnitOfWork unitOfWork)
    : ICommandHandler<RegisterInsuranceProviderCommand, int>
{
    public async Task<Result<int>> HandleAsync(RegisterInsuranceProviderCommand command, CancellationToken cancellationToken)
    {
        var result = InsuranceProvider.Create(
            command.Name, command.ContactPhone, command.ContactEmail, command.Address, command.Website);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var provider = result.Value;
        if (await repository.ExistsByNameAsync(provider.Name, cancellationToken: cancellationToken))
        {
            return InsuranceProviderErrors.NameAlreadyInUse;
        }

        repository.Add(provider);
        // El ID se genera al guardar; el decorador confirma la transacción.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return provider.Id;
    }
}
