using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;
using SGCM.Data.Application;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Commands.AssociateDoctorSpecialty;
using SGCM.Data.Application.People.Commands.DisassociateDoctorSpecialty;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence.Repositories.People;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

using Unit = SGCM.Data.Domain.Common.Unit;

[Trait("Category", "Integration")]
public sealed class DoctorSpecialtyPersistenceTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Guardar_solo_inserta_o_elimina_el_vinculo_solicitado_sin_tocar_perfil_o_auditoria(bool remove)
    {
        await using var seedContext = Context();
        var seed = await DoctorPersistenceData.SeedAsync(seedContext);
        var doctorsBefore = await TestDataSeeder.ReadDoctorsAsync(seedContext, [seed.DoctorId]);
        var linksBefore = await TestDataSeeder.ReadDoctorSpecialtiesAsync(seedContext, [seed.DoctorId]);
        var target = remove ? seed.SpecialtyIds[0] : seed.SpecialtyIds[2];
        await using (var provider = DoctorPersistenceData.Provider(Database.ConnectionString))
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unit.BeginTransactionAsync(CancellationToken.None);
            var doctor = (await scope.ServiceProvider.GetRequiredService<IDoctorRepository>()
                .GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None))!;

            (remove ? doctor.DisassociateSpecialty(target) : doctor.AssociateSpecialty(target)).IsSuccess.ShouldBeTrue();

            context.ChangeTracker.DetectChanges();
            context.Entry(doctor).State.ShouldBe(EntityState.Unchanged);
            context.ChangeTracker.Entries<DoctorSpecialty>()
                .Where(entry => entry.State != EntityState.Unchanged).ShouldHaveSingleItem()
                .State.ShouldBe(remove ? EntityState.Deleted : EntityState.Added);
            await unit.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        await using var verification = Context();
        (await TestDataSeeder.ReadDoctorsAsync(verification, [seed.DoctorId])).ShouldBe(doctorsBefore);
        var linksAfter = await TestDataSeeder.ReadDoctorSpecialtiesAsync(verification, [seed.DoctorId]);
        linksAfter.Select(SpecialtyId).ShouldBe(remove ? [seed.SpecialtyIds[1]] : seed.SpecialtyIds, ignoreOrder: true);
        linksAfter.Where(row => SpecialtyId(row) != target)
            .ShouldBe(linksBefore.Where(row => SpecialtyId(row) != target));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rollback_real_revierte_vinculos_ya_enviados_a_PostgreSQL(bool remove)
    {
        await using var seedContext = Context();
        var seed = await DoctorPersistenceData.SeedAsync(seedContext);
        var before = await TestDataSeeder.ReadDoctorSpecialtiesAsync(seedContext, [seed.DoctorId]);
        var doctors = await TestDataSeeder.ReadDoctorsAsync(seedContext, [seed.DoctorId]);
        await using (var provider = DoctorPersistenceData.Provider(Database.ConnectionString))
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unit.BeginTransactionAsync(CancellationToken.None);
            var doctor = (await scope.ServiceProvider.GetRequiredService<IDoctorRepository>()
                .GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None))!;
            (remove ? doctor.DisassociateSpecialty(seed.SpecialtyIds[0])
                : doctor.AssociateSpecialty(seed.SpecialtyIds[2])).IsSuccess.ShouldBeTrue();

            await unit.SaveChangesAsync(CancellationToken.None);

            (await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, [seed.DoctorId])).Select(SpecialtyId)
                .ShouldBe(remove ? [seed.SpecialtyIds[1]] : seed.SpecialtyIds, ignoreOrder: true);
            await using var outside = Context();
            (await TestDataSeeder.ReadDoctorSpecialtiesAsync(outside, [seed.DoctorId])).ShouldBe(before);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await using var verification = Context();
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(verification, [seed.DoctorId])).ShouldBe(before);
        (await TestDataSeeder.ReadDoctorsAsync(verification, [seed.DoctorId])).ShouldBe(doctors);
    }

    [Theory]
    [InlineData("last", "Doctor.CannotRemoveLastSpecialty")]
    [InlineData("missing", "Doctor.SpecialtyNotAssociated")]
    [InlineData("duplicate", "Doctor.SpecialtyAlreadyAssociated")]
    public async Task Conflictos_no_generan_cambios_ni_actualizaciones_de_auditoria(string operation, string code)
    {
        await using var context = Context();
        var seed = await DoctorPersistenceData.SeedAsync(context, associations: operation == "last" ? 1 : 2);
        var before = await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, [seed.DoctorId]);
        var doctors = await TestDataSeeder.ReadDoctorsAsync(context, [seed.DoctorId]);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var doctor = (await new DoctorRepository(context)
            .GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None))!;

        var result = operation == "duplicate" ? doctor.AssociateSpecialty(seed.SpecialtyIds[0])
            : doctor.DisassociateSpecialty(operation == "last" ? seed.SpecialtyIds[0] : int.MaxValue);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        context.ChangeTracker.HasChanges().ShouldBeFalse();
        (await context.SaveChangesAsync()).ShouldBe(0);
        await transaction.RollbackAsync();
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, [seed.DoctorId])).ShouldBe(before);
        (await TestDataSeeder.ReadDoctorsAsync(context, [seed.DoctorId])).ShouldBe(doctors);
    }

    [Fact]
    public async Task Clave_compuesta_duplicada_real_se_traduce_y_el_rollback_no_deja_filas_adicionales()
    {
        await using var seedContext = Context();
        var seed = await DoctorPersistenceData.SeedAsync(seedContext);
        var before = await TestDataSeeder.ReadDoctorSpecialtiesAsync(seedContext, [seed.DoctorId]);
        await using (var provider = DoctorPersistenceData.Provider(Database.ConnectionString))
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unit.BeginTransactionAsync(CancellationToken.None);
            var doctor = (await scope.ServiceProvider.GetRequiredService<IDoctorRepository>()
                .GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None))!;
            doctor.AssociateSpecialty(seed.SpecialtyIds[2]).IsSuccess.ShouldBeTrue();
            // Simula un escritor SQL ajeno al grafo rastreado y provoca la PK real al guardar.
            (await TestDataSeeder.InsertDoctorSpecialtyAsync(context, seed.DoctorId, seed.SpecialtyIds[2])).ShouldBe(1);

            var conflict = await Should.ThrowAsync<PersistenceConflictException>(() =>
                unit.SaveChangesAsync(CancellationToken.None));

            conflict.Error.ShouldBe(DoctorErrors.SpecialtyAlreadyAssociated);
            conflict.Error.Code.ShouldBe("Doctor.SpecialtyAlreadyAssociated");
            conflict.Error.Type.ShouldBe(ErrorType.Conflict);
            var postgres = conflict.InnerException.ShouldBeOfType<DbUpdateException>()
                .InnerException.ShouldBeOfType<PostgresException>();
            postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            postgres.SchemaName.ShouldBe("people");
            postgres.TableName.ShouldBe("doctor_specialty");
            postgres.ConstraintName.ShouldBe("pk_doctor_specialty");
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await using var verification = Context();
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(verification, [seed.DoctorId])).ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dos_desasociaciones_concurrentes_esperan_y_conservan_la_ultima_incluso_con_carga_obsoleta(bool preload)
    {
        await using var seedContext = Context();
        var seed = await DoctorPersistenceData.SeedAsync(seedContext);
        var before = await TestDataSeeder.ReadDoctorSpecialtiesAsync(seedContext, [seed.DoctorId]);
        var doctors = await TestDataSeeder.ReadDoctorsAsync(seedContext, [seed.DoctorId]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var firstContext = Context();
        await using var secondContext = Context();
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync(timeout.Token);
        var first = (await new DoctorRepository(firstContext).GetByUserIdForUpdateAsync(seed.UserId, timeout.Token))!;
        await secondContext.Database.OpenConnectionAsync(timeout.Token);
        var firstPid = await PidAsync(firstContext, timeout.Token);
        var secondPid = await PidAsync(secondContext, timeout.Token);
        firstPid.ShouldNotBe(secondPid);
        await using var secondTransaction = await secondContext.Database.BeginTransactionAsync(timeout.Token);
        Doctor? stale = preload ? await secondContext.Doctors.SingleAsync(timeout.Token) : null;
        if (preload)
        {
            stale.ShouldNotBeNull();
            stale.Specialties.Select(link => link.SpecialtyId).ShouldBe(seed.SpecialtyIds.Take(2), ignoreOrder: true);
        }
        Task<Doctor?>? secondLoad = null;
        try
        {
            secondLoad = new DoctorRepository(secondContext).GetByUserIdForUpdateAsync(seed.UserId, timeout.Token);
            await WaitForLockAsync(secondPid, firstPid, secondLoad, timeout.Token);
            secondLoad.IsCompleted.ShouldBeFalse();
            first.DisassociateSpecialty(seed.SpecialtyIds[0]).IsSuccess.ShouldBeTrue();
            (await firstContext.SaveChangesAsync(timeout.Token)).ShouldBe(1);
            await firstTransaction.CommitAsync(timeout.Token);
            firstContext.Database.CurrentTransaction.ShouldBeNull();

            var second = await secondLoad.WaitAsync(TimeSpan.FromSeconds(10));
            second.ShouldNotBeNull();
            if (preload) second.ShouldNotBeSameAs(stale);
            second.Specialties.ShouldHaveSingleItem().SpecialtyId.ShouldBe(seed.SpecialtyIds[1]);
            var result = second.DisassociateSpecialty(seed.SpecialtyIds[1]);

            result.Error.ShouldBe(DoctorErrors.CannotRemoveLastSpecialty);
            result.Error.Code.ShouldBe("Doctor.CannotRemoveLastSpecialty");
            result.Error.Type.ShouldBe(ErrorType.Conflict);
            secondContext.ChangeTracker.HasChanges().ShouldBeFalse();
            (await secondContext.SaveChangesAsync(timeout.Token)).ShouldBe(0);
            await secondTransaction.RollbackAsync(timeout.Token);
            (await HasWaitingLockAsync(secondPid, timeout.Token)).ShouldBeFalse();
        }
        finally
        {
            if (firstContext.Database.CurrentTransaction is not null)
                await firstTransaction.RollbackAsync(CancellationToken.None);
            if (secondLoad is not null)
            {
                if (!secondLoad.IsCompleted) await timeout.CancelAsync();
                try { await secondLoad.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            }
            if (secondContext.Database.CurrentTransaction is not null)
                await secondTransaction.RollbackAsync(CancellationToken.None);
        }

        firstContext.Database.CurrentTransaction.ShouldBeNull();
        secondContext.Database.CurrentTransaction.ShouldBeNull();
        await AssertReleasedAsync([firstPid, secondPid], timeout.Token);
        await firstContext.Database.CloseConnectionAsync();
        await secondContext.Database.CloseConnectionAsync();
        firstContext.Database.GetDbConnection().State.ShouldBe(System.Data.ConnectionState.Closed);
        secondContext.Database.GetDbConnection().State.ShouldBe(System.Data.ConnectionState.Closed);
        await using var verification = Context();
        var after = await TestDataSeeder.ReadDoctorSpecialtiesAsync(verification, [seed.DoctorId]);
        after.Select(SpecialtyId).ShouldBe(new[] { seed.SpecialtyIds[1] });
        after.ShouldBe(before.Where(row => SpecialtyId(row) == seed.SpecialtyIds[1]));
        (await TestDataSeeder.ReadDoctorsAsync(verification, [seed.DoctorId])).ShouldBe(doctors);
    }

    [Fact]
    public async Task Cancelar_una_espera_real_libera_la_peticion_sin_borrar_vinculos_ni_dejar_bloqueos()
    {
        await using var seedContext = Context();
        var seed = await DoctorPersistenceData.SeedAsync(seedContext);
        var before = await TestDataSeeder.ReadDoctorSpecialtiesAsync(seedContext, [seed.DoctorId]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var request = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using var first = Context();
        await using var second = Context();
        await using var firstTransaction = await first.Database.BeginTransactionAsync(timeout.Token);
        await new DoctorRepository(first).GetByUserIdForUpdateAsync(seed.UserId, timeout.Token);
        await using var secondTransaction = await second.Database.BeginTransactionAsync(timeout.Token);
        var firstPid = await PidAsync(first, timeout.Token);
        var pid = await PidAsync(second, timeout.Token);
        pid.ShouldNotBe(firstPid);
        var waiting = new DoctorRepository(second).GetByUserIdForUpdateAsync(seed.UserId, request.Token);
        try
        {
            await WaitForLockAsync(pid, firstPid, waiting, timeout.Token);
            waiting.IsCompleted.ShouldBeFalse();
            await request.CancelAsync();

            var error = await Should.ThrowAsync<OperationCanceledException>(() =>
                waiting.WaitAsync(TimeSpan.FromSeconds(10)));
            error.CancellationToken.ShouldBe(request.Token);

            await secondTransaction.RollbackAsync(timeout.Token);
            (await HasWaitingLockAsync(pid, timeout.Token)).ShouldBeFalse();
        }
        finally
        {
            if (first.Database.CurrentTransaction is not null)
                await firstTransaction.RollbackAsync(CancellationToken.None);
            await request.CancelAsync();
            try { await waiting.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            if (second.Database.CurrentTransaction is not null)
                await secondTransaction.RollbackAsync(CancellationToken.None);
        }
        first.Database.CurrentTransaction.ShouldBeNull();
        second.Database.CurrentTransaction.ShouldBeNull();
        await AssertReleasedAsync([firstPid, pid], timeout.Token);
        await first.Database.CloseConnectionAsync();
        await second.Database.CloseConnectionAsync();
        first.Database.GetDbConnection().State.ShouldBe(System.Data.ConnectionState.Closed);
        second.Database.GetDbConnection().State.ShouldBe(System.Data.ConnectionState.Closed);
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(seedContext, [seed.DoctorId])).ShouldBe(before);

        // Otra transacción puede tomar el mismo Doctor y escribir tras cancelar la espera.
        await using (var recovery = Context())
        await using (var transaction = await recovery.Database.BeginTransactionAsync(timeout.Token))
        {
            var doctor = (await new DoctorRepository(recovery).GetByUserIdForUpdateAsync(seed.UserId, timeout.Token))!;
            doctor.DisassociateSpecialty(seed.SpecialtyIds[0]).IsSuccess.ShouldBeTrue();
            (await recovery.SaveChangesAsync(timeout.Token)).ShouldBe(1);
            await transaction.RollbackAsync(timeout.Token);
        }
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(seedContext, [seed.DoctorId])).ShouldBe(before);
    }


    [Theory]
    [InlineData("associate")]
    [InlineData("removeActive")]
    [InlineData("removeInactive")]
    public async Task Comandos_reales_persisten_mediante_el_pipeline_y_solo_modifican_el_perfil_actual(string operation)
    {
        await using var seedContext = Context();
        var seed = await DoctorPersistenceData.SeedAsync(seedContext);
        var control = await DoctorPersistenceData.SeedAsync(seedContext);
        var ids = new[] { seed.DoctorId, control.DoctorId };
        var catalogIds = seed.SpecialtyIds.Concat(control.SpecialtyIds).ToArray();
        var catalog = await ReadCatalogAsync(seedContext, catalogIds);
        var doctors = await TestDataSeeder.ReadDoctorsAsync(seedContext, ids);
        var links = await TestDataSeeder.ReadDoctorSpecialtiesAsync(seedContext, ids);
        var associate = operation == "associate";
        var target = associate ? seed.SpecialtyIds[2]
            : operation == "removeActive" ? seed.SpecialtyIds[0] : seed.SpecialtyIds[1];
        await using (var provider = CommandProvider(seed.UserId))
        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await HandleCommand(scope.ServiceProvider, associate, target);
            result.IsSuccess.ShouldBeTrue();
            result.Value.ShouldBe(Unit.Value);
            scope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeTracker.HasChanges().ShouldBeFalse();
        }

        await using var verification = Context();
        (await TestDataSeeder.ReadDoctorsAsync(verification, ids)).ShouldBe(doctors);
        (await ReadCatalogAsync(verification, catalogIds)).ShouldBe(catalog);
        var after = await TestDataSeeder.ReadDoctorSpecialtiesAsync(verification, ids);
        var own = after.Where(row => DoctorId(row) == seed.DoctorId).ToArray();
        own.Select(SpecialtyId).ShouldBe(associate ? seed.SpecialtyIds
            : operation == "removeActive" ? [seed.SpecialtyIds[1]] : [seed.SpecialtyIds[0]], ignoreOrder: true);
        after.Where(row => DoctorId(row) == control.DoctorId).ShouldBe(links.Where(row => DoctorId(row) == control.DoctorId));
        own.Where(row => SpecialtyId(row) != target)
            .ShouldBe(links.Where(row => DoctorId(row) == seed.DoctorId && SpecialtyId(row) != target));
    }

    [Theory]
    [InlineData(true, "legacy", "Doctor.ProfileIncomplete")]
    [InlineData(false, "legacy", "Doctor.ProfileIncomplete")]
    [InlineData(true, "missingDoctor", "Doctor.NotFound")]
    [InlineData(false, "missingDoctor", "Doctor.NotFound")]
    [InlineData(true, "missingSpecialty", "Specialty.NotFound")]
    [InlineData(true, "inactive", "Specialty.Inactive")]
    [InlineData(true, "duplicate", "Doctor.SpecialtyAlreadyAssociated")]
    [InlineData(false, "last", "Doctor.CannotRemoveLastSpecialty")]
    [InlineData(false, "missingLink", "Doctor.SpecialtyNotAssociated")]
    public async Task Comandos_reales_rechazan_errores_y_legados_sin_cambiar_filas_o_auditoria(
        bool associate, string scenario, string code)
    {
        await using var seedContext = Context();
        var seed = await DoctorPersistenceData.SeedAsync(seedContext,
            associations: scenario == "legacy" ? 0 : scenario == "last" ? 1 : 2);
        var userId = seed.UserId;
        if (scenario == "missingDoctor")
        {
            var user = UserMother.WithRole(UserRole.Doctor,
                email: Email.Create($"missing-{Guid.NewGuid():N}@correo.com").Value);
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
            userId = user.Id;
        }
        var doctors = await TestDataSeeder.ReadDoctorsAsync(seedContext, [seed.DoctorId]);
        var links = await TestDataSeeder.ReadDoctorSpecialtiesAsync(seedContext, [seed.DoctorId]);
        var catalog = await ReadCatalogAsync(seedContext, seed.SpecialtyIds);
        var target = scenario is "missingSpecialty" or "missingLink" ? int.MaxValue
            : scenario == "inactive" ? seed.SpecialtyIds[1]
            : scenario is "last" or "duplicate" ? seed.SpecialtyIds[0] : seed.SpecialtyIds[2];
        await using (var provider = CommandProvider(userId))
        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await HandleCommand(scope.ServiceProvider, associate, target);

            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe(code);
            result.Error.Type.ShouldBe(scenario is "missingDoctor" or "missingSpecialty" ? ErrorType.NotFound : ErrorType.Conflict);
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.ChangeTracker.HasChanges().ShouldBeFalse();
            context.Database.CurrentTransaction.ShouldBeNull();
        }
        await using var verification = Context();
        (await TestDataSeeder.ReadDoctorsAsync(verification, [seed.DoctorId])).ShouldBe(doctors);
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(verification, [seed.DoctorId])).ShouldBe(links);
        (await ReadCatalogAsync(verification, seed.SpecialtyIds)).ShouldBe(catalog);
    }

    private static async Task<string[]> ReadCatalogAsync(AppDbContext context, int[] specialtyIds) =>
        (await context.Database.SqlQuery<string>(
            $"SELECT to_jsonb(s)::text AS \"Value\" FROM people.specialty s WHERE s.id = ANY({specialtyIds}) ORDER BY s.id")
            .ToListAsync()).ToArray();

    private ServiceProvider CommandProvider(int userId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options => options.SgcmDatabase = Database.ConnectionString);
        var current = Substitute.For<ICurrentUser>();
        current.UserId.Returns(userId);
        services.AddSingleton(current);
        services.AddInfrastructure();
        services.AddApplication();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static Task<Result<Unit>> HandleCommand(IServiceProvider provider, bool associate, int id) => associate
        ? provider.GetRequiredService<ICommandHandler<AssociateDoctorSpecialtyCommand, Unit>>()
            .HandleAsync(new(id), CancellationToken.None)
        : provider.GetRequiredService<ICommandHandler<DisassociateDoctorSpecialtyCommand, Unit>>()
            .HandleAsync(new(id), CancellationToken.None);

    private static int DoctorId(string row)
    {
        using var document = JsonDocument.Parse(row);
        return document.RootElement.GetProperty("doctor_id").GetInt32();
    }

    private async Task WaitForLockAsync(int pid, int ownerPid, Task operation, CancellationToken cancellationToken)
    {
        while (!await HasWaitingLockAsync(pid, cancellationToken))
        {
            operation.IsCompleted.ShouldBeFalse("La operación debe esperar el bloqueo del Doctor.");
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
        await using var probe = Context();
        (await probe.Database.SqlQuery<bool>(
            $"SELECT {ownerPid} = ANY(pg_blocking_pids({pid})) AS \"Value\"")
            .SingleAsync(cancellationToken)).ShouldBeTrue();
    }

    private async Task AssertReleasedAsync(int[] pids, CancellationToken cancellationToken)
    {
        await using var probe = Context();
        // Un PID devuelto al pool puede ser reutilizado por esta propia consulta.
        (await probe.Database.SqlQuery<bool>(
            $"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = ANY({pids}) AND pid <> pg_backend_pid() AND xact_start IS NOT NULL) AS \"Value\"")
            .SingleAsync(cancellationToken)).ShouldBeFalse();
        (await probe.Database.SqlQuery<bool>(
            $"SELECT EXISTS (SELECT 1 FROM pg_locks WHERE pid = ANY({pids}) AND pid <> pg_backend_pid() AND locktype IN ('tuple', 'transactionid')) AS \"Value\"")
            .SingleAsync(cancellationToken)).ShouldBeFalse();
    }

    private async Task<bool> HasWaitingLockAsync(int pid, CancellationToken cancellationToken)
    {
        await using var probe = Context();
        return await probe.Database.SqlQuery<bool>(
            $"SELECT EXISTS (SELECT 1 FROM pg_locks WHERE pid = {pid} AND NOT granted) AS \"Value\"")
            .SingleAsync(cancellationToken);
    }

    private static Task<int> PidAsync(AppDbContext context, CancellationToken cancellationToken) =>
        context.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync(cancellationToken);

    private static int SpecialtyId(string row)
    {
        using var document = JsonDocument.Parse(row);
        return document.RootElement.GetProperty("specialty_id").GetInt32();
    }

    private AppDbContext Context() => DoctorPersistenceData.Context(Database.ConnectionString);
}
