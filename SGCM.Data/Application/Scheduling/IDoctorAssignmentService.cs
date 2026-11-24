using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Asigna un médico cuando el paciente agenda eligiendo solo la especialidad.
/// </summary>
public interface IDoctorAssignmentService
{
    /// <summary>
    /// Elige al azar uno de los médicos activos de la especialidad, dentro de los filtros de ubicación, que
    /// tenga libre el horario completo de una cita que empieza en <paramref name="startsAt"/>.
    /// </summary>
    /// <param name="specialtyId">Especialidad elegida por el paciente.</param>
    /// <param name="startsAt">Inicio del horario elegido.</param>
    /// <param name="provinceId">Filtro de provincia; si es <c>null</c> no se filtra.</param>
    /// <param name="municipalityId">Filtro de municipio; si es <c>null</c> no se filtra.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// El id del médico, <see cref="Domain.Scheduling.Appointments.AppointmentErrors.NotOnHour"/> si el inicio no
    /// está en punto, o <see cref="Domain.Scheduling.Appointments.AppointmentErrors.SlotUnavailable"/> si ningún
    /// médico tiene libre ese horario.
    /// </returns>
    Task<Result<int>> AssignAsync(
        int specialtyId, DateTimeOffset startsAt, int? provinceId, int? municipalityId, CancellationToken cancellationToken);
}
