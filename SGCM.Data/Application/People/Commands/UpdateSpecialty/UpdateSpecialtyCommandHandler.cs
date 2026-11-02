using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.UpdateSpecialty;

internal sealed class UpdateSpecialtyCommandHandler(ISpecialtyRepository repository)
    : ICommandHandler<UpdateSpecialtyCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateSpecialtyCommand command, CancellationToken cancellationToken)
    {
        var specialty = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (specialty is null)
        {
            return SpecialtyErrors.NotFound;
        }

        var candidate = Specialty.Create(specialty.Name, specialty.Description);
        if (candidate.IsFailure)
        {
            return candidate.Error;
        }

        // Valida el candidato sin alterar la entidad rastreada antes de comprobar la unicidad.
        var validation = candidate.Value.Update(
            command.NameProvided, command.Name, command.DescriptionProvided, command.Description);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        if (command.NameProvided
            && await repository.ExistsByNameAsync(candidate.Value.Name, specialty.Id, cancellationToken))
        {
            return SpecialtyErrors.NameAlreadyInUse;
        }

        var result = specialty.Update(
            command.NameProvided, command.Name, command.DescriptionProvided, command.Description);

        if (result.IsFailure)
        {
            return result.Error;
        }

        return Unit.Value;
    }
}
