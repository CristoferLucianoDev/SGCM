using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.CreateSpecialty;

internal sealed class CreateSpecialtyCommandHandler(ISpecialtyRepository repository, IUnitOfWork unitOfWork)
    : ICommandHandler<CreateSpecialtyCommand, int>
{
    public async Task<Result<int>> HandleAsync(CreateSpecialtyCommand command, CancellationToken cancellationToken)
    {
        var result = Specialty.Create(command.Name, command.Description);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var specialty = result.Value;
        if (await repository.ExistsByNameAsync(specialty.Name, cancellationToken: cancellationToken))
        {
            return SpecialtyErrors.NameAlreadyInUse;
        }

        repository.Add(specialty);
        // El ID se genera al guardar; el decorador confirma la transacción.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return specialty.Id;
    }
}
