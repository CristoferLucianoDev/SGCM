using SGCM.Data.Application.Abstractions.Notifications;

namespace SGCM.Data.Infrastructure.Notifications.Channels;

internal sealed record RenderedMessage(Recipient Recipient, string Subject, string HtmlBody);