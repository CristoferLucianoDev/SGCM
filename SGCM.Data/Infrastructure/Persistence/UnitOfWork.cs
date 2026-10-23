using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence.Interceptors;

namespace SGCM.Data.Infrastructure.Persistence;

internal sealed class UnitOfWork(AppDbContext context, DomainEventsQueue queue, DomainEventDispatcher dispatcher)
    : IUnitOfWork
{
    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new EfTransaction(await context.Database.BeginTransactionAsync(cancellationToken), queue, dispatcher);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "uq_specialty_name",
            SchemaName: "people",
            TableName: "specialty"
        })
        {
            throw new PersistenceConflictException(SpecialtyErrors.NameAlreadyInUse, exception);
        }
    }

    private sealed class EfTransaction(
        IDbContextTransaction transaction,
        DomainEventsQueue queue,
        DomainEventDispatcher dispatcher) : ITransaction
    {
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await transaction.CommitAsync(cancellationToken);
            await dispatcher.DispatchAsync(queue.DequeueAll());
        }

        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            await transaction.RollbackAsync(cancellationToken);
            queue.Clear();
        }

        // Si se libera sin commit (por una excepción, por ejemplo), los eventos pendientes se descartan.
        public ValueTask DisposeAsync()
        {
            queue.Clear();
            return transaction.DisposeAsync();
        }
    }
}
