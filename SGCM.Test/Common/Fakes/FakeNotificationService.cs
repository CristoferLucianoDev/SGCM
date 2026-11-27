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

    /// <summary>
    /// Si se indica, las notificaciones que cumplen la condición fallan como si el servicio de correo estuviera caído y
    /// no quedan en <see cref="Sent"/>. <see cref="Clear"/> lo quita.
    /// </summary>
    public Func<Notification, bool>? FailWhen { get; set; }

    public Task<Result> SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (FailWhen?.Invoke(notification) == true)
            return Task.FromResult(Result.Failure(Error.Failure("Notification.DeliveryFailed", "Servicio de correo no disponible.")));

        _sent.Enqueue(notification);
        return Task.FromResult(Result.Success());
    }

    /// <summary>
    /// Descarta las notificaciones guardadas y quita la falla simulada.
    /// </summary>
    public void Clear()
    {
        _sent.Clear();
        FailWhen = null;
    }
}