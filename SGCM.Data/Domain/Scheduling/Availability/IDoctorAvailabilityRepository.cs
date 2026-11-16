using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Availability;

public interface IDoctorAvailabilityRepository : IRepository<DoctorAvailability>
{
    /// <summary>Agrega varios bloques, por ejemplo los que genera una serie hasta el horizonte.</summary>
    void AddRange(IEnumerable<DoctorAvailability> blocks);
}
