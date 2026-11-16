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
}
