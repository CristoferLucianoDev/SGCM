using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Exceptions;

namespace SGCM.Data.Domain.Messaging.ReminderTemplates;

/// <summary>
/// Plantilla de recordatorio con variables como {PatientName}. Con <see cref="DoctorId"/> nulo es global: la ven todos
/// los médicos, pero nadie la edita ni la desactiva. Si no, es privada de ese médico, que la crea, la edita y la
/// desactiva. Editarla no cambia los recordatorios ya programados, que guardan su texto final.
/// </summary>
public sealed class ReminderTemplate : ActivatableAggregateRoot
{
    /// <summary>Nombre de la plantilla global, sembrada en la base, que usa el recordatorio automático.</summary>
    public const string AutomaticReminderName = "Recordatorio de cita";

    public const int NameMaxLength = 100;
    public const int SubjectMaxLength = 200;
    public const int BodyTemplateMaxLength = 2000;

    /// <summary>
    /// Variables que admiten el asunto y el cuerpo, las mismas que permite ck_reminder_template_variables en la base.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedVariables =
        ["PatientName", "DoctorName", "AppointmentDate", "StartTime", "SpecialtyName"];

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

    public bool IsGlobal => DoctorId is null;

    protected override string EntityName => "ReminderTemplate";

    /// <summary>Crea una plantilla activa y privada del médico. Recorta los espacios de los extremos.</summary>
    /// <returns>La plantilla, o el primer error de nombre, asunto o cuerpo.</returns>
    /// <exception cref="DomainException">Si <paramref name="doctorId"/> no es mayor que cero.</exception>
    public static Result<ReminderTemplate> Create(int doctorId, string? name, string? subject, string? bodyTemplate)
    {
        if (doctorId <= 0) throw new DomainException("El médico de la plantilla debe tener un id válido.");

        var template = new ReminderTemplate { DoctorId = doctorId };
        var applied = template.Apply(name, subject, bodyTemplate);
        return applied.IsFailure ? applied.Error : template;
    }

    /// <summary>Indica si el médico puede usarla: debe estar activa y ser global o suya.</summary>
    public bool IsUsableBy(int doctorId) => IsActive && (IsGlobal || DoctorId == doctorId);

    /// <summary>Indica si el médico la gestiona: solo las suyas, no las globales ni las de otro médico.</summary>
    public bool IsOwnedBy(int doctorId) => DoctorId == doctorId;

    /// <summary>Reemplaza nombre, asunto y cuerpo con las mismas reglas que al crearla.</summary>
    /// <returns>Éxito; <see cref="ReminderTemplateErrors.GlobalReadOnly"/> si es global; o el primer error de los datos.</returns>
    public Result Update(string? name, string? subject, string? bodyTemplate)
    {
        if (IsGlobal) return ReminderTemplateErrors.GlobalReadOnly;

        return Apply(name, subject, bodyTemplate);
    }

    /// <summary>Una plantilla global no se activa ni se desactiva.</summary>
    protected override Result EnsureActivationCanChange() =>
        IsGlobal ? ReminderTemplateErrors.GlobalReadOnly : Result.Success();

    /// <summary>
    /// Valida y asigna los tres datos de una vez: si uno falla no cambia nada.
    /// </summary>
    private Result Apply(string? name, string? subject, string? bodyTemplate)
    {
        var trimmedName = name?.Trim();
        var trimmedSubject = subject?.Trim();
        var trimmedBody = bodyTemplate?.Trim();

        if (string.IsNullOrEmpty(trimmedName)) return ReminderTemplateErrors.NameRequired;
        if (trimmedName.Length > NameMaxLength) return ReminderTemplateErrors.NameTooLong;
        if (string.IsNullOrEmpty(trimmedSubject)) return ReminderTemplateErrors.SubjectRequired;
        if (trimmedSubject.Length > SubjectMaxLength) return ReminderTemplateErrors.SubjectTooLong;
        if (string.IsNullOrEmpty(trimmedBody)) return ReminderTemplateErrors.BodyRequired;
        if (trimmedBody.Length > BodyTemplateMaxLength) return ReminderTemplateErrors.BodyTooLong;

        var unknown = TemplatePlaceholders.FindUnknown(trimmedSubject, AllowedVariables)
            .Concat(TemplatePlaceholders.FindUnknown(trimmedBody, AllowedVariables))
            .FirstOrDefault();
        if (unknown is not null) return ReminderTemplateErrors.UnknownVariable(unknown);

        Name = trimmedName;
        Subject = trimmedSubject;
        BodyTemplate = trimmedBody;
        return Result.Success();
    }
}
