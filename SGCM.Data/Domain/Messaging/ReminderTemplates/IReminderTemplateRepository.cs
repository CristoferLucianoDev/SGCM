namespace SGCM.Data.Domain.Messaging.ReminderTemplates;

/// <summary>Lectura y escritura de las plantillas de recordatorio.</summary>
public interface IReminderTemplateRepository
{
    /// <summary>La plantilla global activa con ese nombre, o <c>null</c> si no existe o está inactiva.</summary>
    Task<ReminderTemplate?> GetActiveGlobalByNameAsync(string name, CancellationToken cancellationToken);

    /// <summary>La plantilla con ese id, activa o no, o <c>null</c> si no existe.</summary>
    Task<ReminderTemplate?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// La plantilla con ese id, activa o no, con seguimiento para editarla o desactivarla; <c>null</c> si no existe.
    /// </summary>
    Task<ReminderTemplate?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken);

    void Add(ReminderTemplate template);
}
