using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Availability;

public interface IDoctorAvailabilityRepository : IRepository<DoctorAvailability>
{
    /// <summary>Busca un bloque por su id, rastreado para poder cambiarlo.</summary>
    /// <returns>El bloque, o <c>null</c> si no existe.</returns>
    Task<DoctorAvailability?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Agrega varios bloques, por ejemplo los que genera una serie hasta el horizonte.</summary>
    void AddRange(IEnumerable<DoctorAvailability> blocks);

    /// <summary>
    /// Marca el bloque para borrarlo al guardar. La base rechaza borrar un bloque con citas activas dentro, así que
    /// quien lo use cancela antes esas citas.
    /// </summary>
    void Remove(DoctorAvailability block);

    /// <summary>
    /// Bloques del médico, abiertos o cerrados y sin rastrear, que terminan después de <paramref name="from"/>
    /// y, si se indica, empiezan antes de <paramref name="to"/>.
    /// </summary>
    Task<IReadOnlyList<DoctorAvailability>> ListByDoctorAsync(
        int doctorId, DateTimeOffset from, DateTimeOffset? to, CancellationToken cancellationToken);

    /// <summary>
    /// Bloques de esos médicos, abiertos o cerrados y sin rastrear, que terminan después de
    /// <paramref name="from"/> y empiezan antes de <paramref name="to"/>, ordenados por inicio. Sin ids devuelve
    /// una lista vacía sin consultar la base.
    /// </summary>
    Task<IReadOnlyList<DoctorAvailability>> ListByDoctorsAsync(
        IReadOnlyCollection<int> doctorIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>
    /// Bloquea, hasta el fin de la transacción, el bloque abierto del médico que contiene [inicio, fin). Dos
    /// solicitudes para el mismo bloque se ordenan aquí, así que la segunda vuelve a comprobar el horario después
    /// de que la primera confirme su cita.
    /// </summary>
    /// <returns><c>true</c> si el médico tiene un bloque abierto que contiene el rango.</returns>
    Task<bool> LockOpenBlockAsync(
        int doctorId, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken);
}
