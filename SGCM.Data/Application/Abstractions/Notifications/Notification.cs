namespace SGCM.Data.Application.Abstractions.Notifications;

public abstract record Notification(Recipient Recipient)
{
    public abstract string TemplateKey { get; }

    public abstract IReadOnlyDictionary<string, string> GetValues();
}