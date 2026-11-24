using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.RescheduleAvailabilityBlock;

/// <summary>
/// Reprograma un bloque propio. Comprueba la regla de semanas (en la semana actual el error indica que el bloque
/// solo se puede cerrar), el traslape con los demás bloques del médico y, con la política de citas afectadas, el
/// motivo para las citas que quedan fuera del horario nuevo. Esas citas se cancelan y se guardan antes de mover el
/// bloque, porque la base de datos no deja mover un bloque que deja fuera citas activas. Un bloque de otro médico se
/// trata como inexistente.
/// </summary>
internal sealed class RescheduleAvailabilityBlockCommandHandler(
    ICurrentUser currentUser,
    IDoctorRepository doctorRepository,
    IDoctorAvailabilityRepository availabilityRepository,
    IAffectedAppointmentsPolicy policy,
    IAffectedAppointmentsCanceler canceler,
    IDateTimeProvider clock,
    BusinessCalendar calendar,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<RescheduleAvailabilityBlockCommand, RescheduledAvailabilityBlockDto>
{
    public async Task<Result<RescheduledAvailabilityBlockDto>> HandleAsync(
        RescheduleAvailabilityBlockCommand command, CancellationToken cancellationToken)
    {
        if (!command.TryGetUtcRange(calendar, out var startsAt, out var endsAt))
            return DoctorAvailabilityErrors.InvalidTimeRange;

        var doctor = await doctorRepository.GetByUserIdForUpdateAsync(currentUser.UserId, cancellationToken);
        if (doctor is null) return DoctorErrors.NotFound;

        var block = await availabilityRepository.GetByIdAsync(command.BlockId, cancellationToken);
        if (block is null || block.DoctorId != doctor.Id) return DoctorAvailabilityErrors.NotFound;

        var now = clock.UtcNow;
        var minimumHours = schedulingOptions.Value.MinimumBlockDurationHours;
        var valid = block.ValidateReschedule(startsAt, endsAt, minimumHours, now, calendar);
        if (valid != Error.None) return valid;

        // Bloquea el bloque como la solicitud de cita, para que nadie reserve en él mientras se reprograma.
        await availabilityRepository.LockOpenBlockAsync(doctor.Id, block.StartsAt, block.EndsAt, cancellationToken);

        // La consulta devuelve los bloques que se cruzan con [inicio, fin), abiertos o cerrados; el propio no cuenta.
        var overlapping = await availabilityRepository.ListByDoctorAsync(doctor.Id, startsAt, endsAt, cancellationToken);
        if (overlapping.Any(other => other.Id != block.Id)) return DoctorAvailabilityErrors.Overlap;

        var removed = TimeRange.Create(block.StartsAt, block.EndsAt).Value
            .Subtract(TimeRange.Create(startsAt, endsAt).Value);
        var affected = await policy.EvaluateAsync(doctor.Id, removed, cancellationToken);
        var reason = affected.ValidateReason(command.Reason);
        if (reason != Error.None) return reason;

        var cancelled = await canceler.CancelAsync(doctor.Id, removed, command.Reason, cancellationToken);
        if (cancelled.IsFailure) return cancelled.Error;

        var rescheduled = block.Reschedule(startsAt, endsAt, minimumHours, now, calendar, command.Reason);
        if (rescheduled.IsFailure) return rescheduled.Error;

        // El decorador guarda el horario nuevo y confirma la transacción; el correo a cada paciente sale después del commit.
        return new RescheduledAvailabilityBlockDto(cancelled.Value);
    }
}
