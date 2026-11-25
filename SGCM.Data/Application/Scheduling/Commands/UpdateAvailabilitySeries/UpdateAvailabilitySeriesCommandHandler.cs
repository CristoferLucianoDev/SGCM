using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.UpdateAvailabilitySeries;

/// <summary>
/// Cambia la recurrencia de una serie propia desde la primera fecha que la regla de semanas deja reprogramar, así que
/// la semana actual no cambia. Los bloques futuros de la serie que no se editaron a mano ni se cerraron se ajustan: el
/// de un día que sigue en el patrón toma el horario nuevo y el de un día que sale del patrón se borra; después
/// <see cref="AvailabilityBlockGenerator"/> crea los de los días nuevos. Las citas activas que quedan fuera se cancelan
/// y se guardan antes de tocar los bloques, porque la base no deja mover ni borrar un bloque que deja fuera citas
/// activas. Una serie de otro médico se trata como inexistente.
/// </summary>
internal sealed class UpdateAvailabilitySeriesCommandHandler(
    ICurrentUser currentUser,
    IDoctorRepository doctorRepository,
    IDoctorAvailabilitySeriesRepository seriesRepository,
    IDoctorAvailabilityRepository availabilityRepository,
    IAffectedAppointmentsPolicy policy,
    IAffectedAppointmentsCanceler canceler,
    AvailabilityBlockGenerator generator,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    BusinessCalendar calendar,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<UpdateAvailabilitySeriesCommand, UpdatedAvailabilitySeriesDto>
{
    private sealed record BlockChange(DoctorAvailability Block, AvailabilityOccurrence? Occurrence, IReadOnlyList<TimeRange> Removed);

    public async Task<Result<UpdatedAvailabilitySeriesDto>> HandleAsync(
        UpdateAvailabilitySeriesCommand command, CancellationToken cancellationToken)
    {
        var minimumHours = schedulingOptions.Value.MinimumBlockDurationHours;
        var days = CreateAvailabilitySeriesCommandHandler.ToDaysOfWeek(command.DaysOfWeek);
        if (days.IsFailure) return days.Error;

        var doctor = await doctorRepository.GetByUserIdForUpdateAsync(currentUser.UserId, cancellationToken);
        if (doctor is null) return DoctorErrors.NotFound;

        var series = await seriesRepository.GetByIdAsync(command.SeriesId, cancellationToken);
        if (series is null || series.DoctorId != doctor.Id) return DoctorAvailabilitySeriesErrors.NotFound;

        var now = clock.UtcNow;
        var updated = series.Update(
            days.Value, command.StartTime, command.EndTime, command.EndDate,
            RescheduleWeekRule.FirstReschedulableDate(now, calendar), minimumHours);
        if (updated.IsFailure) return updated.Error;

        var otherSeries = await seriesRepository.ListActiveByDoctorAsync(doctor.Id, cancellationToken);
        if (otherSeries.Any(other => other.Id != series.Id && series.OverlapsWith(other)))
            return DoctorAvailabilitySeriesErrors.Overlap;

        // Un día de margen cubre la diferencia entre UTC y la zona de la serie; se filtra después por fecha local.
        var from = new DateTimeOffset(series.StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var blocks = (await availabilityRepository.ListByDoctorAsync(doctor.Id, from, null, cancellationToken))
            .Where(block => series.LocalDateOf(block.StartsAt) >= series.StartDate)
            .ToList();

        // Los bloques propios de la serie no cuentan: los editados a mano o cerrados se conservan y ese día no se genera.
        if (blocks.Any(block => block.SeriesId != series.Id && series.OverlapsWith(block.StartsAt, block.EndsAt)))
            return DoctorAvailabilitySeriesErrors.Overlap;

        var changes = PlanChanges(series, blocks);
        IReadOnlyCollection<TimeRange> removed = changes.SelectMany(change => change.Removed).ToList();

        // Bloquea los bloques que cambian, como la solicitud de cita, para que nadie reserve en ellos mientras tanto.
        foreach (var change in changes)
            await availabilityRepository.LockOpenBlockAsync(doctor.Id, change.Block.StartsAt, change.Block.EndsAt, cancellationToken);

        var affected = await policy.EvaluateAsync(doctor.Id, removed, cancellationToken);
        var reason = affected.ValidateReason(command.Reason);
        if (reason != Error.None) return reason;

        var cancelled = await canceler.CancelAsync(doctor.Id, removed, command.Reason, cancellationToken);
        if (cancelled.IsFailure) return cancelled.Error;

        var applied = await ApplyAsync(changes, minimumHours, now, command.Reason, cancellationToken);
        if (applied != Error.None) return applied;

        // El generador lee los bloques de la base, así que primero se guardan los cambios.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await generator.GenerateAsync(series, cancellationToken);

        // El decorador guarda los bloques nuevos y confirma la transacción; el correo a cada paciente sale después.
        return new UpdatedAvailabilitySeriesDto(series.StartDate, cancelled.Value);
    }

    // Solo cambian los bloques abiertos y no editados a mano: el de un día que sigue en el patrón toma el horario nuevo
    // (si es distinto) y el de un día que sale del patrón se borra.
    private static List<BlockChange> PlanChanges(DoctorAvailabilitySeries series, IReadOnlyList<DoctorAvailability> blocks)
    {
        var editable = blocks.Where(block => block.SeriesId == series.Id && block.IsAvailable && !block.IsModified).ToList();
        if (editable.Count == 0) return [];

        var lastDate = editable.Max(block => series.LocalDateOf(block.StartsAt));
        var occurrences = series.GetOccurrences(series.StartDate, lastDate).ToDictionary(occurrence => occurrence.Date);

        var changes = new List<BlockChange>();
        foreach (var block in editable)
        {
            var current = TimeRange.Create(block.StartsAt, block.EndsAt).Value;
            if (!occurrences.TryGetValue(series.LocalDateOf(block.StartsAt), out var occurrence))
            {
                changes.Add(new BlockChange(block, null, [current]));
                continue;
            }

            if (occurrence.StartsAt == block.StartsAt && occurrence.EndsAt == block.EndsAt) continue;
            changes.Add(new BlockChange(block, occurrence, current.Subtract(occurrence.Range)));
        }

        return changes;
    }

    private async Task<Error> ApplyAsync(
        IReadOnlyList<BlockChange> changes, int minimumHours, DateTimeOffset now, string? reason, CancellationToken cancellationToken)
    {
        foreach (var change in changes)
        {
            // La lista de bloques no se rastrea; para cambiarlos se cargan rastreados.
            var block = await availabilityRepository.GetByIdAsync(change.Block.Id, cancellationToken);
            if (block is null) return DoctorAvailabilityErrors.NotFound;

            if (change.Occurrence is null)
            {
                availabilityRepository.Remove(block);
                continue;
            }

            var rescheduled = block.Reschedule(
                change.Occurrence.StartsAt, change.Occurrence.EndsAt, minimumHours, now, calendar, reason);
            if (rescheduled.IsFailure) return rescheduled.Error;
        }

        return Error.None;
    }
}
