using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Messaging.ReminderTemplates;

/// <summary>
/// Plantilla de recordatorio con variables como {PatientName}. Con <see cref="DoctorId"/> nulo es global y la ven
/// todos los médicos; si no, es privada de ese médico. En este PBI solo se lee, para usar la global del recordatorio
/// automático.
/// </summary>
public sealed class ReminderTemplate : AggregateRoot
{
    /// <summary>Nombre de la plantilla global, sembrada en la base, que usa el recordatorio automático.</summary>
    public const string AutomaticReminderName = "Recordatorio de cita";

    public const int NameMaxLength = 100;
    public const int SubjectMaxLength = 200;
    public const int BodyTemplateMaxLength = 2000;

    private ReminderTemplate()
    {
    }

    /// <summary>Médico dueño de la plantilla; <c>null</c> si es global.</summary>
    public int? DoctorId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Asunto, con variables.</summary>
    public string Subject { get; private set; } = null!;

    /// <summary>Texto, con variables.</summary>
    public string BodyTemplate { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public bool IsGlobal => DoctorId is null;
}
