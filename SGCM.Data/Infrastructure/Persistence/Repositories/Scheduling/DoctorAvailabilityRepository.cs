using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Scheduling;

internal sealed class DoctorAvailabilityRepository(AppDbContext context) : IDoctorAvailabilityRepository
{
    public void Add(DoctorAvailability block) => context.DoctorAvailabilities.Add(block);

    public void AddRange(IEnumerable<DoctorAvailability> blocks) => context.DoctorAvailabilities.AddRange(blocks);
}
