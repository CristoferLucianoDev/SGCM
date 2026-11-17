using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Availability;

public interface IDoctorAvailabilitySeriesRepository : IRepository<DoctorAvailabilitySeries>
{
    /// <summary>Serie rastreada para modificarla, o <c>null</c> si no existe.</summary>
    Task<DoctorAvailabilitySeries?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Series activas del médico, sin rastrear, para comprobar traslapes.</summary>
    Task<IReadOnlyList<DoctorAvailabilitySeries>> ListActiveByDoctorAsync(int doctorId, CancellationToken cancellationToken);

    /// <summary>
    /// Series activas de todos los médicos que siguen vigentes en <paramref name="date"/> o después,
    /// sin rastrear, para generar sus bloques.
    /// </summary>
    Task<IReadOnlyList<DoctorAvailabilitySeries>> ListActiveFromAsync(DateOnly date, CancellationToken cancellationToken);
}
