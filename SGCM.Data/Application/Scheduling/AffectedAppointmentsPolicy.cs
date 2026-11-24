using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Implementación de <see cref="IAffectedAppointmentsPolicy"/>: lee una sola vez las citas activas del médico que
/// tocan los rangos y aplica <see cref="AffectedAppointments.Evaluate"/> con la regla de cancelación tardía de la
/// configuración, la misma que usa la cancelación de una cita por el médico.
/// </summary>
internal sealed class AffectedAppointmentsPolicy(
    IActiveAppointmentReader appointmentReader,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions) : IAffectedAppointmentsPolicy
{
    public async Task<AffectedAppointments> EvaluateAsync(
        int doctorId, IReadOnlyCollection<TimeRange> removedRanges, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(removedRanges);
        var options = schedulingOptions.Value;

        IReadOnlyList<BookedInterval> active = [];
        if (removedRanges.Count > 0)
        {
            var from = removedRanges.Min(range => range.Start);
            var to = removedRanges.Max(range => range.End);
            var byDoctor = await appointmentReader.ListActiveIntervalsAsync([doctorId], from, to, cancellationToken);
            active = byDoctor.GetValueOrDefault(doctorId) ?? [];
        }

        return AffectedAppointments.Evaluate(active, removedRanges, clock.UtcNow, options.ToShortNoticeCancellation());
    }
}
