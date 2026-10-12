using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;
using SGCM.Web.Jobs;

namespace SGCM.Test.Unit.Web;

public sealed class RecurringJobTests : IDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ShortInterval = TimeSpan.FromMilliseconds(50);

    private readonly ICommandHandler<TestCommand, int> _handler = Substitute.For<ICommandHandler<TestCommand, int>>();
    private readonly ServiceProvider _provider;
    private TestJob? _job;

    public RecurringJobTests()
    {
        _provider = new ServiceCollection()
            .AddSingleton(_handler)
            .BuildServiceProvider();
    }

    [Fact]
    public async Task RunOnceAsync_ExecutesTheHandler()
    {
        _handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>()).Returns(Result<int>.Success(3));
        var job = CreateJob(enabled: true, LongInterval);

        await job.RunOnceAsync(CancellationToken.None);

        await _handler.Received(1).HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_HandlerThrows_DoesNotThrow()
    {
        _handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<int>>>(_ => throw new InvalidOperationException());
        var job = CreateJob(enabled: true, LongInterval);

        await Should.NotThrowAsync(() => job.RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RunOnceAsync_HandlerFails_DoesNotThrow()
    {
        _handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(Error.Failure("Test.Failure", "Fallo de prueba.")));
        var job = CreateJob(enabled: true, LongInterval);

        await Should.NotThrowAsync(() => job.RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Disabled_NeverRunsTheHandler()
    {
        var job = CreateJob(enabled: false, LongInterval);

        await job.StartAsync(CancellationToken.None);
        await job.ExecuteTask!;

        await _handler.DidNotReceive().HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Enabled_RunsOnStart()
    {
        var executed = new TaskCompletionSource();
        _handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                executed.TrySetResult();
                return Result<int>.Success(0);
            });
        var job = CreateJob(enabled: true, LongInterval);

        await job.StartAsync(CancellationToken.None);
        await executed.Task.WaitAsync(WaitTimeout);
        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsRunningAfterAFailure()
    {
        var recovered = new TaskCompletionSource();
        _handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new InvalidOperationException(),
                _ =>
                {
                    recovered.TrySetResult();
                    return Result<int>.Success(0);
                });
        var job = CreateJob(enabled: true, ShortInterval);

        await job.StartAsync(CancellationToken.None);
        await recovered.Task.WaitAsync(WaitTimeout);
        await job.StopAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        _job?.Dispose();
        _provider.Dispose();
    }

    private TestJob CreateJob(bool enabled, TimeSpan interval)
    {
        var options = Options.Create(new WorkerOptions
        {
            Enabled = enabled,
            Intervals = new WorkerIntervalsOptions { PurgeUnconfirmedAccounts = interval }
        });

        _job = new TestJob(_provider.GetRequiredService<IServiceScopeFactory>(), options, Serilog.Core.Logger.None);
        return _job;
    }

    public sealed record TestCommand : ICommand<int>;

    public sealed class TestJob(IServiceScopeFactory scopeFactory, IOptions<WorkerOptions> options, Serilog.ILogger logger)
        : RecurringJob<TestCommand>(scopeFactory, options, logger)
    {
        protected override TimeSpan GetInterval(WorkerIntervalsOptions intervals) => intervals.PurgeUnconfirmedAccounts;
    }
}
