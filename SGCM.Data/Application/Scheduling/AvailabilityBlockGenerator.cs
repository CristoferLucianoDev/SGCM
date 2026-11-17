using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Genera los bloques de una serie desde hoy hasta el horizonte de agenda. Es idempotente: omite los días
/// que ya tienen un bloque de la serie (aunque se haya cerrado o editado) y los que chocarían con otro bloque
/// del médico, así que puede ejecutarse las veces que haga falta. Solo agrega los bloques; guarda quien lo usa.
/// </summary>
internal sealed class AvailabilityBlockGenerator(
    IDoctorAvailabilityRepository availabilityRepository,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions)
{
    /// <returns>Cuántos bloques nuevos agregó.</returns>
    public async Task<int> GenerateAsync(DoctorAvailabilitySeries series, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (!series.IsActive) return 0;

        var options = schedulingOptions.Value;
        var today = series.LocalDateOf(clock.UtcNow);
        var occurrences = series.GetOccurrences(today, today.AddDays(options.BookingHorizonDays));
        if (occurrences.Count == 0) return 0;

        var existing = await availabilityRepository.ListByDoctorAsync(
            series.DoctorId, occurrences[0].StartsAt, occurrences[^1].EndsAt, cancellationToken);
        var generatedDays = existing
            .Where(block => block.SeriesId == series.Id)
            .Select(block => series.LocalDateOf(block.StartsAt))
            .ToHashSet();

        var blocks = new List<DoctorAvailability>();
        foreach (var occurrence in occurrences)
        {
            if (generatedDays.Contains(occurrence.Date)) continue;
            if (existing.Any(block => occurrence.Range.Overlaps(block.StartsAt, block.EndsAt))) continue;

            var block = DoctorAvailability.CreateFromSeries(series, occurrence, options.MinimumBlockDurationHours);
            if (block.IsSuccess) blocks.Add(block.Value);
        }

        if (blocks.Count > 0) availabilityRepository.AddRange(blocks);
        return blocks.Count;
    }
}
