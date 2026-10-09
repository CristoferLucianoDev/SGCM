using System.Globalization;
using SGCM.Data.Application.Abstractions.Notifications;

namespace SGCM.Data.Application.Identity.Notifications;

public sealed record AccountConfirmationNotification(
    Recipient Recipient,
    string ConfirmationUrl,
    int ExpirationMinutes) : Notification(Recipient)
{
    public override string TemplateKey => "account-confirmation";

    public override IReadOnlyDictionary<string, string> GetValues() =>
        new Dictionary<string, string>
        {
            ["ConfirmationUrl"] = ConfirmationUrl,
            ["ExpirationMinutes"] = ExpirationMinutes.ToString(CultureInfo.InvariantCulture)
        };
}