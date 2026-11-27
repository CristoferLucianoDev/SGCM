using SGCM.Data.Application.Abstractions.Notifications;

namespace SGCM.Data.Application.Messaging.Notifications;

/// <summary>
/// Correo de un recordatorio. El asunto y el texto ya vienen finales: el texto es el que se guardó al programarlo y
/// el correo lo codifica como HTML al enviarlo.
/// </summary>
public sealed record ReminderNotification(Recipient Recipient, string Subject, string Message) : Notification(Recipient)
{
    public const string ReminderTemplateKey = "appointment-reminder";

    public override string TemplateKey => ReminderTemplateKey;

    public override IReadOnlyDictionary<string, string> GetValues() =>
        new Dictionary<string, string>
        {
            ["Subject"] = Subject,
            ["Message"] = Message
        };
}
