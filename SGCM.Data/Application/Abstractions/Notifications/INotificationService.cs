using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Abstractions.Notifications;

public interface INotificationService
{
    Task<Result> SendAsync(Notification notification, CancellationToken cancellationToken);
}