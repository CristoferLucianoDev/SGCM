using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
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
        catch (DbUpdateException exception) when (TryGetConflict(exception, out var error))
        {
            throw new PersistenceConflictException(error, exception);
        }
    }

    // Índices y restricciones únicas que el dominio reconoce como conflicto. La clave exige esquema,
    // tabla y nombre para no traducir por error una violación de otra tabla.
    private static readonly Dictionary<(string? Schema, string? Table, string Constraint), Error> UniqueConflicts = new()
    {
        [("people", "specialty", "uq_specialty_name")] = SpecialtyErrors.NameAlreadyInUse,
        [("people", "patient", "uq_patient_user_id")] = PatientErrors.ProfileAlreadyExists,
        [("people", "doctor", "uq_doctor_user_id")] = DoctorErrors.ProfileAlreadyExists,
        [("people", "doctor", "uq_doctor_license_number")] = DoctorErrors.LicenseNumberAlreadyInUse,
        [("people", "doctor_specialty", "pk_doctor_specialty")] = DoctorErrors.SpecialtyAlreadyAssociated,
        [("people", "insurance_provider", "uq_insurance_provider_name")] = InsuranceProviderErrors.NameAlreadyInUse
    };

    // Restricciones EXCLUDE que el dominio reconoce como conflicto, con la misma clave exacta.
    private static readonly Dictionary<(string? Schema, string? Table, string Constraint), Error> ExclusionConflicts = new()
    {
        [("scheduling", "doctor_availability", "ex_doctor_availability_no_overlap")] = DoctorAvailabilityErrors.Overlap,
        [("scheduling", "appointment", "ex_appointment_doctor_no_overlap")] = AppointmentErrors.SlotUnavailable,
        [("scheduling", "appointment", "ex_appointment_patient_no_overlap")] = AppointmentErrors.PatientOverlap
    };

    // Reglas de triggers que el dominio reconoce como conflicto. Un trigger solo informa el nombre de la regla, sin
    // esquema ni tabla, por eso esos dos campos van en null. El del bloque solo ocurre si el bloque se cerró o cambió
    // a la vez, al agendar o al extender: el horario deja de estar disponible.
    private static readonly Dictionary<(string? Schema, string? Table, string Constraint), Error> CheckConflicts = new()
    {
        [(null, null, "tg_appointment_within_block")] = AppointmentErrors.SlotUnavailable,
        // Solo ocurre si el reloj de la aplicación va adelantado respecto al de la base: la cita está bloqueada, así
        // que su estado no cambia mientras se registra.
        [(null, null, "tg_medical_record_appointment_started")] = AppointmentErrors.ClinicalRecordNotStarted
    };

    private static bool TryGetConflict(DbUpdateException exception, out Error error)
    {
        if (exception.InnerException is PostgresException
            {
                SqlState: var sqlState,
                SchemaName: var schema,
                TableName: var table,
                ConstraintName: { } constraint
            }
            && ConflictsFor(sqlState) is { } conflicts
            && conflicts.TryGetValue((schema, table, constraint), out var conflict))
        {
            error = conflict;
            return true;
        }

        error = Error.None;
        return false;
    }

    private static Dictionary<(string? Schema, string? Table, string Constraint), Error>? ConflictsFor(string sqlState) =>
        sqlState switch
        {
            PostgresErrorCodes.UniqueViolation => UniqueConflicts,
            PostgresErrorCodes.ExclusionViolation => ExclusionConflicts,
            PostgresErrorCodes.CheckViolation => CheckConflicts,
            _ => null
        };

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
