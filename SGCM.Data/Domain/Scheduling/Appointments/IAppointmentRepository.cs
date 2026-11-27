using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Appointments;

public interface IAppointmentRepository : IRepository<Appointment>
{
    /// <summary>Busca una cita por su id, rastreada para poder cambiarla.</summary>
    /// <returns>La cita, o <c>null</c> si no existe.</returns>
    Task<Appointment?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Bloquea la cita con SELECT ... FOR UPDATE y la carga rastreada. Requiere una transacción activa.
    /// Si la cita ya estaba rastreada sin cambios, se descarta esa copia y se vuelve a leer después del
    /// bloqueo: así, si otra transacción la cambió mientras esta esperaba, se lee su estado nuevo.
    /// </summary>
    /// <returns>La cita, o <c>null</c> si no existe.</returns>
    /// <exception cref="InvalidOperationException">
    /// Si no hay una transacción activa o la cita ya rastreada tiene cambios pendientes.
    /// </exception>
    Task<Appointment?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Lista los ids de las citas pendientes cuyo plazo de confirmación ya pasó (starts_at - horas &lt; now, es decir
    /// starts_at &lt; now + horas), ordenados por inicio y por id. Devuelve como máximo <paramref name="maxCount"/>
    /// ids, los más antiguos primero. Es un prefiltro sin bloqueo: la regla final la aplica
    /// <see cref="Appointment.Expire"/> sobre la cita bloqueada.
    /// </summary>
    /// <param name="now">Instante actual.</param>
    /// <param name="confirmationDeadlineHours">Horas de antelación mínima para confirmar, según la configuración.</param>
    /// <param name="maxCount">Cantidad máxima de ids que se devuelven.</param>
    /// <returns>Los ids candidatos; vacío si no hay ninguno.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="maxCount"/> no es positivo.</exception>
    Task<IReadOnlyList<int>> ListExpirableIdsAsync(
        DateTimeOffset now, int confirmationDeadlineHours, int maxCount, CancellationToken cancellationToken);

    /// <summary>
    /// Lista los ids de las citas confirmadas que ya llegaron a su fin (ends_at &lt;= now), ordenados por fin y por id.
    /// Devuelve como máximo <paramref name="maxCount"/> ids, los más antiguos primero. Es un prefiltro sin bloqueo:
    /// la regla final la aplica <see cref="Appointment.CompleteFinished"/> sobre la cita bloqueada.
    /// </summary>
    /// <param name="now">Instante actual.</param>
    /// <param name="maxCount">Cantidad máxima de ids que se devuelven.</param>
    /// <returns>Los ids candidatos; vacío si no hay ninguno.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="maxCount"/> no es positivo.</exception>
    Task<IReadOnlyList<int>> ListFinishedIdsAsync(DateTimeOffset now, int maxCount, CancellationToken cancellationToken);
}
