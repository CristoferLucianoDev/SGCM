namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>
/// Resuelve el id de un médico activo sin cargar su perfil completo.
/// </summary>
public interface IActiveDoctorReader
{
    /// <summary>
    /// Busca un médico por su id.
    /// </summary>
    /// <param name="doctorId">Id del perfil de médico.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>El id si el médico existe y está activo; si no, <c>null</c>.</returns>
    Task<int?> FindActiveDoctorIdAsync(int doctorId, CancellationToken cancellationToken);

    /// <summary>
    /// Busca el perfil de médico de un usuario.
    /// </summary>
    /// <param name="userId">Id del usuario dueño del perfil.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>El id del perfil de médico activo del usuario; si no, <c>null</c>.</returns>
    Task<int?> FindActiveDoctorIdByUserIdAsync(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Lista los médicos activos de una especialidad, con filtros opcionales de ubicación.
    /// </summary>
    /// <param name="specialtyId">Id de la especialidad.</param>
    /// <param name="provinceId">Provincia del médico; si es <c>null</c> no se filtra.</param>
    /// <param name="municipalityId">Municipio del médico; si es <c>null</c> no se filtra. Se combina con la provincia con AND.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// Los ids ordenados de los médicos activos que tienen esa especialidad, solo si está activa, y que cumplen
    /// los filtros; lista vacía si la especialidad no existe o está inactiva.
    /// </returns>
    Task<IReadOnlyList<int>> ListActiveDoctorIdsBySpecialtyAsync(
        int specialtyId, int? provinceId, int? municipalityId, CancellationToken cancellationToken);
}
