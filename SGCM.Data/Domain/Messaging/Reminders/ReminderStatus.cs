namespace SGCM.Data.Domain.Messaging.Reminders;

/// <summary>Estado de entrega de un recordatorio; la base lo guarda en minúsculas (ck_reminder_delivery_status).</summary>
public enum ReminderStatus
{
    /// <summary>Programado y todavía sin enviar; es el único estado que puede cambiar.</summary>
    Pending,

    /// <summary>Enviado al paciente.</summary>
    Sent,

    /// <summary>El envío falló; queda registrado y no se reintenta.</summary>
    Failed,

    /// <summary>Anulado antes de enviarse, por ejemplo porque la cita se canceló.</summary>
    Cancelled
}
