using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;

/// <summary>
/// Crea el bloque bajo el bloqueo del perfil del médico, para que dos solicitudes simultáneas no pasen a la
/// vez la comprobación de traslapes. Si aun así chocan, la restricción de la base lo convierte en conflicto.
/// </summary>
internal sealed class CreateAvailabilityBlockCommandHandler(
    ICurrentUser currentUser,
    IDoctorRepository doctorRepository,
    IDoctorAvailabilityRepository availabilityRepository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    BusinessCalendar calendar,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<CreateAvailabilityBlockCommand, int>
{
    public async Task<Result<int>> HandleAsync(CreateAvailabilityBlockCommand command, CancellationToken cancellationToken)
    {
        if (!command.TryGetUtcRange(calendar, out var startsAt, out var endsAt))
            return DoctorAvailabilityErrors.InvalidTimeRange;

        var now = clock.UtcNow;
        if (startsAt <= now) return DoctorAvailabilityErrors.StartsInPast;

        var doctor = await doctorRepository.GetByUserIdForUpdateAsync(currentUser.UserId, cancellationToken);
        if (doctor is null) return DoctorErrors.NotFound;

        var created = DoctorAvailability.CreateLoose(
            doctor.Id, startsAt, endsAt, schedulingOptions.Value.MinimumBlockDurationHours, now);
        if (created.IsFailure) return created.Error;
        var block = created.Value;

        // La consulta ya devuelve solo los bloques que se cruzan con [inicio, fin), abiertos o cerrados.
        var overlapping = await availabilityRepository.ListByDoctorAsync(
            doctor.Id, block.StartsAt, block.EndsAt, cancellationToken);
        if (overlapping.Count > 0) return DoctorAvailabilityErrors.Overlap;

        availabilityRepository.Add(block);
        // El ID se genera al guardar; el decorador confirma la transacción.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return block.Id;
    }
}
