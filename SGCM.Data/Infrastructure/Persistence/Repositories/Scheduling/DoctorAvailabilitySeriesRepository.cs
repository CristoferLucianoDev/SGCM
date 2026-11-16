using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Scheduling;

internal sealed class DoctorAvailabilitySeriesRepository(AppDbContext context) : IDoctorAvailabilitySeriesRepository
{
    public Task<DoctorAvailabilitySeries?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        context.DoctorAvailabilitySeries.AsTracking().SingleOrDefaultAsync(series => series.Id == id, cancellationToken);

    public void Add(DoctorAvailabilitySeries series) => context.DoctorAvailabilitySeries.Add(series);
}
