using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Availability;

public interface IDoctorAvailabilityRepository : IRepository<DoctorAvailability>
{
    /// <summary>Agrega varios bloques, por ejemplo los que genera una serie hasta el horizonte.</summary>
    void AddRange(IEnumerable<DoctorAvailability> blocks);

    /// <summary>
    /// Bloques del médico, abiertos o cerrados y sin rastrear, que terminan después de <paramref name="from"/>
    /// y, si se indica, empiezan antes de <paramref name="to"/>.
    /// </summary>
    Task<IReadOnlyList<DoctorAvailability>> ListByDoctorAsync(
        int doctorId, DateTimeOffset from, DateTimeOffset? to, CancellationToken cancellationToken);
}
