using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Identity.Users.Events;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public class UnitOfWorkTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private readonly RecordingHandler _handler = new();

    [Fact]
    public async Task Sin_transaccion_publica_los_eventos_al_guardar()
    {
        await using var scope = CreateScope();
        var (users, unitOfWork) = Resolve(scope);

        users.Add(UserMother.Pending());
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        _handler.Received.ShouldHaveSingleItem().RawToken.ShouldBe(UserMother.DefaultRawToken);
    }

    [Fact]
    public async Task Con_transaccion_publica_los_eventos_solo_despues_del_commit()
    {
        await using var scope = CreateScope();
        var (users, unitOfWork) = Resolve(scope);

        await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);
        users.Add(UserMother.Pending());
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        _handler.Received.ShouldBeEmpty();

        await transaction.CommitAsync(CancellationToken.None);

        _handler.Received.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Con_rollback_no_guarda_ni_publica_los_eventos()
    {
        await using (var scope = CreateScope())
        {
            var (users, unitOfWork) = Resolve(scope);

            await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);
            users.Add(UserMother.Pending());
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        _handler.Received.ShouldBeEmpty();

        await using var verification = CreateScope();
        var saved = await verification.ServiceProvider.GetRequiredService<IUserRepository>()
            .GetByEmailAsync(UserMother.DefaultEmail, CancellationToken.None);
        saved.ShouldBeNull();
    }

    [Fact]
    public async Task Un_evento_no_se_publica_dos_veces()
    {
        await using var scope = CreateScope();
        var (users, unitOfWork) = Resolve(scope);

        users.Add(UserMother.Pending());
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        _handler.Received.ShouldHaveSingleItem();
    }

    private AsyncServiceScope CreateScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options => options.SgcmDatabase = Database.ConnectionString);
        services.AddInfrastructure();
        services.AddSingleton<IDomainEventHandler<UserTokenIssuedEvent>>(_handler);

        return services.BuildServiceProvider().CreateAsyncScope();
    }

    private static (IUserRepository Users, IUnitOfWork UnitOfWork) Resolve(AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IUserRepository>(),
         scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

    private sealed class RecordingHandler : IDomainEventHandler<UserTokenIssuedEvent>
    {
        public List<UserTokenIssuedEvent> Received { get; } = [];

        public Task HandleAsync(UserTokenIssuedEvent domainEvent, CancellationToken cancellationToken)
        {
            Received.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}