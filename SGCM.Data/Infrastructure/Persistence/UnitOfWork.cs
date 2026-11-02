using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Patients;
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
        catch (DbUpdateException exception) when (TryGetUniqueConflict(exception, out var error))
        {
            throw new PersistenceConflictException(error, exception);
        }
    }

    // Índices únicos que el dominio reconoce como conflicto. La clave exige esquema, tabla e índice
    // para no traducir por error una violación de otra tabla.
    private static readonly Dictionary<(string Schema, string Table, string Constraint), Error> UniqueConflicts = new()
    {
        [("people", "specialty", "uq_specialty_name")] = SpecialtyErrors.NameAlreadyInUse,
        [("people", "patient", "uq_patient_user_id")] = PatientErrors.ProfileAlreadyExists,
        [("people", "doctor_specialty", "pk_doctor_specialty")] = DoctorErrors.SpecialtyAlreadyAssociated
    };

    private static bool TryGetUniqueConflict(DbUpdateException exception, out Error error)
    {
        if (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                SchemaName: { } schema,
                TableName: { } table,
                ConstraintName: { } constraint
            }
            && UniqueConflicts.TryGetValue((schema, table, constraint), out var conflict))
        {
            error = conflict;
            return true;
        }

        error = Error.None;
        return false;
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
