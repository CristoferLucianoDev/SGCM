using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.ActivateSpecialty;

internal sealed class ActivateSpecialtyCommandHandler(ISpecialtyRepository repository)
    : ICommandHandler<ActivateSpecialtyCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ActivateSpecialtyCommand command, CancellationToken cancellationToken)
    {
        var specialty = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (specialty is null)
        {
            return SpecialtyErrors.NotFound;
        }

        var result = specialty.Activate();
        if (result.IsFailure)
        {
            return result.Error;
        }

        return Unit.Value;
    }
}
