using System.Collections.Concurrent;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Domain.Common;

namespace SGCM.Test.Common.Fakes;

/// <summary>
/// Reemplaza el envío real: guarda las notificaciones para que la prueba verifique qué se envió.
/// </summary>
public sealed class FakeNotificationService : INotificationService
{
    private readonly ConcurrentQueue<Notification> _sent = new();

    public IReadOnlyCollection<Notification> Sent => _sent.ToArray();

    public Task<Result> SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        _sent.Enqueue(notification);
        return Task.FromResult(Result.Success());
    }

    /// <summary>
    /// Descarta las notificaciones guardadas.
    /// </summary>
    public void Clear() => _sent.Clear();
}