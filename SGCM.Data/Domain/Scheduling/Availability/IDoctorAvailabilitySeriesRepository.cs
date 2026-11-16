using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Availability;

public interface IDoctorAvailabilitySeriesRepository : IRepository<DoctorAvailabilitySeries>
{
    /// <summary>Serie rastreada para modificarla, o <c>null</c> si no existe.</summary>
    Task<DoctorAvailabilitySeries?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
