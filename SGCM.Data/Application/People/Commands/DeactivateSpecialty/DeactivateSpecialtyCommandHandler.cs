using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.DeactivateSpecialty;

internal sealed class DeactivateSpecialtyCommandHandler(ISpecialtyRepository repository)
    : ICommandHandler<DeactivateSpecialtyCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeactivateSpecialtyCommand command, CancellationToken cancellationToken)
    {
        var specialty = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (specialty is null)
        {
            return SpecialtyErrors.NotFound;
        }

        var result = specialty.Deactivate();
        if (result.IsFailure)
        {
            return result.Error;
        }

        return Unit.Value;
    }
}
