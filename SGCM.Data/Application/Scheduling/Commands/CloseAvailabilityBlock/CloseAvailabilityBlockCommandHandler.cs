using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.CloseAvailabilityBlock;

/// <summary>
/// Cierra un bloque propio. Aplica la política de citas afectadas y rechaza el cierre sin motivo cuando hay citas
/// activas. Primero cancela esas citas con el cancelador, que las guarda, y después cierra el bloque: el trigger T4
/// rechaza cerrar un bloque que todavía tiene citas activas. Un bloque de otro médico se trata como inexistente.
/// </summary>
internal sealed class CloseAvailabilityBlockCommandHandler(
    ICurrentUser currentUser,
    IDoctorRepository doctorRepository,
    IDoctorAvailabilityRepository availabilityRepository,
    IAffectedAppointmentsPolicy policy,
    IAffectedAppointmentsCanceler canceler,
    IDateTimeProvider clock) : ICommandHandler<CloseAvailabilityBlockCommand, ClosedAvailabilityBlockDto>
{
    public async Task<Result<ClosedAvailabilityBlockDto>> HandleAsync(
        CloseAvailabilityBlockCommand command, CancellationToken cancellationToken)
    {
        var doctor = await doctorRepository.GetByUserIdForUpdateAsync(currentUser.UserId, cancellationToken);
        if (doctor is null) return DoctorErrors.NotFound;

        var block = await availabilityRepository.GetByIdAsync(command.BlockId, cancellationToken);
        if (block is null || block.DoctorId != doctor.Id) return DoctorAvailabilityErrors.NotFound;

        var now = clock.UtcNow;
        var closable = block.ValidateClose(now);
        if (closable != Error.None) return closable;

        // Bloquea el bloque como la solicitud de cita, para que nadie reserve en él mientras se cierra.
        await availabilityRepository.LockOpenBlockAsync(doctor.Id, block.StartsAt, block.EndsAt, cancellationToken);

        IReadOnlyCollection<TimeRange> removed = [TimeRange.Create(block.StartsAt, block.EndsAt).Value];
        var affected = await policy.EvaluateAsync(doctor.Id, removed, cancellationToken);
        var reason = affected.ValidateReason(command.Reason);
        if (reason != Error.None) return reason;

        var cancelled = await canceler.CancelAsync(doctor.Id, removed, command.Reason, cancellationToken);
        if (cancelled.IsFailure) return cancelled.Error;

        var closed = block.Close(command.Reason, now);
        if (closed.IsFailure) return closed.Error;

        // El decorador guarda el cierre y confirma la transacción; el correo a cada paciente sale después del commit.
        return new ClosedAvailabilityBlockDto(cancelled.Value);
    }
}
