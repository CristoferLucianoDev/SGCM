namespace SGCM.Data.Domain.Messaging.Reminders;

/// <summary>Persistencia de los recordatorios. Los que se devuelven quedan rastreados para cambiar su estado.</summary>
public interface IReminderRepository
{
    void Add(Reminder reminder);

    /// <summary>Recordatorios pendientes de una cita, en orden de envío.</summary>
    Task<IReadOnlyList<Reminder>> ListPendingByAppointmentAsync(int appointmentId, CancellationToken cancellationToken);

    /// <summary>Hasta <paramref name="limit"/> recordatorios pendientes cuya hora llegó, del más antiguo al más nuevo.</summary>
    Task<IReadOnlyList<Reminder>> ListDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
}
