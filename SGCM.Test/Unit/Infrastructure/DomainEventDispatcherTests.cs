using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common.Interfaces;
using SGCM.Data.Infrastructure.Persistence.Interceptors;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

/// <summary>
/// Pruebas de <see cref="DomainEventDispatcher"/>: cada evento llega a todos sus manejadores en el orden de registro, y
/// un manejador que falla se registra en el log sin detener a los demás ni propagar la excepción.
/// </summary>
public sealed class DomainEventDispatcherTests
{
    private readonly List<string> _calls = [];
    private readonly LogProbe _log = new();

    [Fact]
    public async Task Cada_evento_llega_a_todos_sus_manejadores_en_orden()
    {
        var dispatcher = Dispatcher(new Recorder("primero", _calls), new Recorder("segundo", _calls));

        await dispatcher.DispatchAsync([new SomethingHappened(1), new SomethingHappened(2)]);

        _calls.ShouldBe(["primero:1", "segundo:1", "primero:2", "segundo:2"]);
        _log.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Si_un_manejador_falla_se_registra_en_el_log_y_los_demas_siguen()
    {
        var dispatcher = Dispatcher(new Failing(), new Recorder("segundo", _calls));

        await Should.NotThrowAsync(() => dispatcher.DispatchAsync([new SomethingHappened(1), new SomethingHappened(2)]));

        _calls.ShouldBe(["segundo:1", "segundo:2"]);
        _log.Entries.Count.ShouldBe(2);
        var entry = _log.Entries.First();
        entry.Level.ShouldBe(LogEventLevel.Error);
        entry.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("Servicio caído");
        entry.Properties["Handler"].ShouldBe(new ScalarValue(nameof(Failing)));
        entry.Properties["Event"].ShouldBe(new ScalarValue(nameof(SomethingHappened)));
    }

    [Fact]
    public async Task Un_evento_sin_manejadores_no_hace_nada()
    {
        await Should.NotThrowAsync(() => Dispatcher().DispatchAsync([new SomethingHappened(1)]));

        _log.Entries.ShouldBeEmpty();
    }

    private DomainEventDispatcher Dispatcher(params IDomainEventHandler<SomethingHappened>[] handlers)
    {
        var services = new ServiceCollection();
        foreach (var handler in handlers) services.AddSingleton(handler);
        var logger = new LoggerConfiguration().WriteTo.Sink(_log).CreateLogger();

        return new DomainEventDispatcher(services.BuildServiceProvider(), logger);
    }

    public sealed record SomethingHappened(int Number) : IDomainEvent;

    private sealed class Recorder(string name, List<string> calls) : IDomainEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened domainEvent, CancellationToken cancellationToken)
        {
            calls.Add($"{name}:{domainEvent.Number}");
            return Task.CompletedTask;
        }
    }

    private sealed class Failing : IDomainEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened domainEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Servicio caído");
    }

    private sealed class LogProbe : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Entries { get; } = new();

        public void Emit(LogEvent logEvent) => Entries.Enqueue(logEvent);
    }
}
