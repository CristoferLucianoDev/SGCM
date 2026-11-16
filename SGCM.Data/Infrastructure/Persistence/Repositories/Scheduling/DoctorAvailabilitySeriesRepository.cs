using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Scheduling;

internal sealed class DoctorAvailabilitySeriesRepository(AppDbContext context) : IDoctorAvailabilitySeriesRepository
{
    public Task<DoctorAvailabilitySeries?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        context.DoctorAvailabilitySeries.AsTracking().SingleOrDefaultAsync(series => series.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DoctorAvailabilitySeries>> ListActiveByDoctorAsync(
        int doctorId, CancellationToken cancellationToken) =>
        await context.DoctorAvailabilitySeries.AsNoTracking()
            .Where(series => series.DoctorId == doctorId && series.IsActive)
            .OrderBy(series => series.Id)
            .ToListAsync(cancellationToken);

    public void Add(DoctorAvailabilitySeries series) => context.DoctorAvailabilitySeries.Add(series);
}
