using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Scheduling;

internal sealed class DoctorAvailabilityRepository(AppDbContext context) : IDoctorAvailabilityRepository
{
    public void Add(DoctorAvailability block) => context.DoctorAvailabilities.Add(block);

    public void AddRange(IEnumerable<DoctorAvailability> blocks) => context.DoctorAvailabilities.AddRange(blocks);

    public async Task<IReadOnlyList<DoctorAvailability>> ListByDoctorAsync(
        int doctorId, DateTimeOffset from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to?.ToUniversalTime();

        return await context.DoctorAvailabilities.AsNoTracking()
            .Where(block => block.DoctorId == doctorId && block.EndsAt > fromUtc
                && (toUtc == null || block.StartsAt < toUtc))
            .OrderBy(block => block.StartsAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorAvailability>> ListByDoctorsAsync(
        IReadOnlyCollection<int> doctorIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (doctorIds.Count == 0) return [];

        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        var ids = doctorIds.ToArray();

        return await context.DoctorAvailabilities.AsNoTracking()
            .Where(block => ids.Contains(block.DoctorId) && block.EndsAt > fromUtc && block.StartsAt < toUtc)
            .OrderBy(block => block.StartsAt)
            .ThenBy(block => block.Id)
            .ToListAsync(cancellationToken);
    }
}
