using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Behaviors;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.People;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

using Unit = SGCM.Data.Domain.Common.Unit;

[Trait("Category", "Integration")]
[Collection(DatabaseCollection.Name)]
public class SpecialtyRepositoryTests(PostgresFixture database)
{
    [Fact]
    public async Task ExistsByNameAsync_sin_nombre_existente_devuelve_false() =>
        await InTransactionAsync(async (_, repository, _) =>
        {
            (await repository.ExistsByNameAsync("Cardiología")).ShouldBeFalse();
        });

    [Theory]
    [InlineData("Cardiología", true)]
    [InlineData("CARDIOLOGÍA", true)]
    [InlineData("cardiologia", true)]
    [InlineData("  CARDIOLOGIA  ", true)]
    [InlineData("cardiologia", false)]
    public async Task ExistsByNameAsync_compara_nombres_equivalentes_incluso_inactivos(string name, bool isActive) =>
        await InTransactionAsync(async (_, repository, unitOfWork) =>
        {
            var specialty = Specialty.Create("Cardiología", "Atención del corazón.").Value;
            if (!isActive)
            {
                specialty.Deactivate().IsSuccess.ShouldBeTrue();
            }

            repository.Add(specialty);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            (await repository.ExistsByNameAsync(name)).ShouldBeTrue();
        });

    [Fact]
    public async Task ExistsByNameAsync_excluye_solo_el_id_indicado() =>
        await InTransactionAsync(async (_, repository, unitOfWork) =>
        {
            var specialty = Specialty.Create("Cardiología", "Atención del corazón.").Value;
            repository.Add(specialty);
            repository.Add(Specialty.Create("Neurología", "Atención neurológica.").Value);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            (await repository.ExistsByNameAsync("cardiologia", excludeId: specialty.Id)).ShouldBeFalse();
            (await repository.ExistsByNameAsync("cardiologia", excludeId: null)).ShouldBeTrue();
            (await repository.ExistsByNameAsync("cardiologia", excludeId: specialty.Id + 1000)).ShouldBeTrue();
            (await repository.ExistsByNameAsync("neurologia", excludeId: specialty.Id)).ShouldBeTrue();
        });

    [Fact]
    public async Task ExistsByNameAsync_respeta_la_cancelacion() =>
        await InTransactionAsync(async (_, repository, _) =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Should.ThrowAsync<OperationCanceledException>(() =>
                repository.ExistsByNameAsync("Cardiología", cancellationToken: cancellation.Token));
        });

    [Fact]
    public async Task Add_deja_el_agregado_pendiente_y_UnitOfWork_lo_persiste() =>
        await InTransactionAsync(async (context, repository, unitOfWork) =>
        {
            var specialty = Specialty.Create("CARDIOLOGÍA", "Atención  del corazón.").Value;
            repository.Add(specialty);

            context.Entry(specialty).State.ShouldBe(EntityState.Added);
            specialty.Id.ShouldBe(0);
            (await context.Specialties.AnyAsync()).ShouldBeFalse();

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            specialty.Id.ShouldBeGreaterThan(0);
            context.ChangeTracker.Clear();
            var loaded = await context.Specialties.SingleAsync(value => value.Id == specialty.Id);

            loaded.Name.ShouldBe("Cardiología");
            loaded.Description.ShouldBe("Atención  del corazón.");
            loaded.IsActive.ShouldBeTrue();
        });

    [Fact]
    public async Task AddInfrastructure_registra_el_repositorio_por_scope()
    {
        await using var provider = CreateProvider();
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var repository = first.ServiceProvider.GetRequiredService<ISpecialtyRepository>();

        repository.ShouldBeOfType<SpecialtyRepository>();
        repository.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<ISpecialtyRepository>());
        repository.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<ISpecialtyRepository>());
    }

    [Fact]
    public async Task SaveChangesAsync_traduce_el_indice_unico_con_su_excepcion_original() =>
        await InTransactionAsync(async (_, repository, unitOfWork) =>
        {
            repository.Add(Specialty.Create("Cardiología", "Primera descripción.").Value);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            repository.Add(Specialty.Create("CARDIOLOGIA", "Segunda descripción.").Value);

            var conflict = await Should.ThrowAsync<PersistenceConflictException>(() =>
                unitOfWork.SaveChangesAsync(CancellationToken.None));

            conflict.Error.ShouldBe(SpecialtyErrors.NameAlreadyInUse);
            var update = conflict.InnerException.ShouldBeOfType<DbUpdateException>();
            var postgres = update.InnerException.ShouldBeOfType<PostgresException>();
            postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            postgres.ConstraintName.ShouldBe("uq_specialty_name");
            postgres.SchemaName.ShouldBe("people");
            postgres.TableName.ShouldBe("specialty");
        });

    [Fact]
    public async Task SaveChangesAsync_propaga_una_violacion_unica_de_usuarios() =>
        await InTransactionAsync(async (context, _, unitOfWork) =>
        {
            var email = Email.Create($"{Guid.NewGuid():N}@correo.com").Value;
            context.Users.Add(User.CreateDoctor(email, "hash-de-prueba"));
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            context.Users.Add(User.CreateDoctor(email, "otro-hash-de-prueba"));

            var update = await Should.ThrowAsync<DbUpdateException>(() =>
                unitOfWork.SaveChangesAsync(CancellationToken.None));

            var postgres = update.InnerException.ShouldBeOfType<PostgresException>();
            postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            postgres.ConstraintName.ShouldNotBe("uq_specialty_name");
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransactionDecorator_devuelve_el_conflicto_y_revierte_toda_la_operacion(bool saveInHandler)
    {
        await using var provider = CreateProvider();
        var suffix = $"a{Guid.NewGuid():N}";
        var name = $"Cardiología {suffix}";
        var otherName = $"Neurología {suffix}";
        await using (var scope = provider.CreateAsyncScope())
        {
            var (_, repository, unitOfWork) = Resolve(scope.ServiceProvider);
            var decorator = new TransactionDecorator<TestCommand, Unit>(
                new AddDuplicatesHandler(repository, unitOfWork, name, otherName, saveInHandler), unitOfWork);

            var result = await decorator.HandleAsync(new TestCommand(), CancellationToken.None);

            result.Error.ShouldBe(SpecialtyErrors.NameAlreadyInUse);
        }

        await using var verification = provider.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Specialties.AnyAsync(value =>
            PostgresFunctions.NormalizeText(value.Name) == PostgresFunctions.NormalizeText(name)
            || PostgresFunctions.NormalizeText(value.Name) == PostgresFunctions.NormalizeText(otherName))).ShouldBeFalse();
    }

    [Fact]
    public async Task Dos_creaciones_concurrentes_solo_confirman_una_especialidad()
    {
        await using var provider = CreateProvider();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var name = $"Cardiología {Guid.NewGuid():N}";
        try
        {
            await using var firstScope = provider.CreateAsyncScope();
            await using var secondScope = provider.CreateAsyncScope();
            var (firstContext, firstRepository, firstUnitOfWork) = Resolve(firstScope.ServiceProvider);
            var (secondContext, secondRepository, secondUnitOfWork) = Resolve(secondScope.ServiceProvider);
            await using var firstTransaction = await firstUnitOfWork.BeginTransactionAsync(timeout.Token);
            await using var secondTransaction = await secondUnitOfWork.BeginTransactionAsync(timeout.Token);

            (await firstRepository.ExistsByNameAsync(name, cancellationToken: timeout.Token)).ShouldBeFalse();
            (await secondRepository.ExistsByNameAsync(name, cancellationToken: timeout.Token)).ShouldBeFalse();
            firstRepository.Add(Specialty.Create(name, "Primera creación.").Value);
            secondRepository.Add(Specialty.Create(name.Replace("Cardiología", "CARDIOLOGIA"), "Segunda creación.").Value);
            await firstUnitOfWork.SaveChangesAsync(timeout.Token);
            var secondPid = await secondContext.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
                .SingleAsync(timeout.Token);

            var pendingSave = SaveAndCaptureExceptionAsync(secondUnitOfWork, timeout.Token);
            try
            {
                await WaitForLockAsync(firstContext, secondPid, timeout.Token);
                await firstTransaction.CommitAsync(timeout.Token);

                var conflict = (await pendingSave).ShouldBeOfType<PersistenceConflictException>();
                conflict.Error.ShouldBe(SpecialtyErrors.NameAlreadyInUse);
                await secondTransaction.RollbackAsync(CancellationToken.None);

                await using var verification = provider.CreateAsyncScope();
                var context = verification.ServiceProvider.GetRequiredService<AppDbContext>();
                (await context.Specialties.CountAsync(value =>
                    PostgresFunctions.NormalizeText(value.Name) == PostgresFunctions.NormalizeText(name), timeout.Token)).ShouldBe(1);
            }
            finally
            {
                await timeout.CancelAsync();
                await pendingSave;
            }
        }
        finally
        {
            // La prueba confirma una fila para liberar el bloqueo; solo se elimina su nombre único.
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var cleanup = provider.CreateAsyncScope();
            var context = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Specialties.Where(value =>
                    PostgresFunctions.NormalizeText(value.Name) == PostgresFunctions.NormalizeText(name))
                .ExecuteDeleteAsync(cleanupTimeout.Token);
        }
    }

    private async Task InTransactionAsync(Func<AppDbContext, ISpecialtyRepository, IUnitOfWork, Task> assertion)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var (context, repository, unitOfWork) = Resolve(scope.ServiceProvider);
        await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);

        await assertion(context, repository, unitOfWork);
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options => options.SgcmDatabase = database.ConnectionString);
        services.AddInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static (AppDbContext Context, ISpecialtyRepository Repository, IUnitOfWork UnitOfWork) Resolve(IServiceProvider services) =>
        (services.GetRequiredService<AppDbContext>(), services.GetRequiredService<ISpecialtyRepository>(),
            services.GetRequiredService<IUnitOfWork>());

    private static async Task WaitForLockAsync(AppDbContext context, int pid, CancellationToken cancellationToken)
    {
        while (await context.Database.SqlQuery<int>(
                   $"SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE pid = {pid} AND NOT granted")
               .SingleAsync(cancellationToken) == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }

    private static async Task<Exception?> SaveAndCaptureExceptionAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed record TestCommand : ICommand<Unit>;

    private sealed class AddDuplicatesHandler(
        ISpecialtyRepository repository, IUnitOfWork unitOfWork, string name, string otherName, bool saveInHandler)
        : ICommandHandler<TestCommand, Unit>
    {
        public async Task<Result<Unit>> HandleAsync(TestCommand command, CancellationToken cancellationToken)
        {
            repository.Add(Specialty.Create(otherName, "Otra especialidad.").Value);
            // El conflicto posterior también debe revertir este primer guardado.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            repository.Add(Specialty.Create(name, "Primera creación.").Value);
            repository.Add(Specialty.Create(name.Replace("Cardiología", "CARDIOLOGIA"), "Duplicada.").Value);
            if (saveInHandler)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Unit.Value;
        }
    }
}
