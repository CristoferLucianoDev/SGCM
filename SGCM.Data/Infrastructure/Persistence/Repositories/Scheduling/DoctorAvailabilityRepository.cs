using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Scheduling;

internal sealed class DoctorAvailabilityRepository(AppDbContext context) : IDoctorAvailabilityRepository
{
    public Task<DoctorAvailability?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        context.DoctorAvailabilities.AsTracking().SingleOrDefaultAsync(block => block.Id == id, cancellationToken);

    public void Add(DoctorAvailability block) => context.DoctorAvailabilities.Add(block);

    public void AddRange(IEnumerable<DoctorAvailability> blocks) => context.DoctorAvailabilities.AddRange(blocks);

    public void Remove(DoctorAvailability block) => context.DoctorAvailabilities.Remove(block);

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

    public async Task<bool> LockOpenBlockAsync(
        int doctorId, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken)
    {
        var fromUtc = startsAt.ToUniversalTime();
        var toUtc = endsAt.ToUniversalTime();

        // FOR UPDATE solo tiene efecto dentro de una transacción; la abre el decorador del comando.
        var ids = await context.Database.SqlQuery<int>($"""
            SELECT id AS "Value"
            FROM scheduling.doctor_availability
            WHERE doctor_id = {doctorId}
              AND is_available
              AND starts_at <= {fromUtc}
              AND ends_at >= {toUtc}
            FOR UPDATE
            """).ToListAsync(cancellationToken);

        return ids.Count > 0;
    }
}
