using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.People;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public class PatientRepositoryTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task ExistsByUserIdAsync_sin_perfil_devuelve_false()
    {
        var userId = await SaveUserAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var (_, repository, _) = Resolve(scope.ServiceProvider);

        (await repository.ExistsByUserIdAsync(userId)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistsByUserIdAsync_con_perfil_devuelve_true_incluso_inactivo(bool isActive)
    {
        var userId = await SaveUserAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var (_, repository, unitOfWork) = Resolve(scope.ServiceProvider);
        var patient = PatientMother.WithRequiredData(userId);
        if (!isActive)
        {
            patient.Deactivate().IsSuccess.ShouldBeTrue();
        }

        repository.Add(patient);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        (await repository.ExistsByUserIdAsync(userId)).ShouldBeTrue();
    }

    [Fact]
    public async Task ExistsByUserIdAsync_no_confunde_el_perfil_de_otro_usuario()
    {
        var userId = await SaveUserAsync();
        var otherUserId = await SaveUserAsync("otro@correo.com");
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var (_, repository, unitOfWork) = Resolve(scope.ServiceProvider);
        repository.Add(PatientMother.WithRequiredData(otherUserId));
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        (await repository.ExistsByUserIdAsync(userId)).ShouldBeFalse();
    }

    [Fact]
    public async Task ExistsByUserIdAsync_respeta_la_cancelacion()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var (_, repository, _) = Resolve(scope.ServiceProvider);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            repository.ExistsByUserIdAsync(1, cancellation.Token));
    }

    [Fact]
    public async Task Add_deja_el_agregado_pendiente_y_UnitOfWork_lo_persiste()
    {
        var userId = await SaveUserAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var (context, repository, unitOfWork) = Resolve(scope.ServiceProvider);
        var patient = PatientMother.WithRequiredData(userId);

        repository.Add(patient);

        context.Entry(patient).State.ShouldBe(EntityState.Added);
        patient.Id.ShouldBe(0);
        (await context.Patients.AnyAsync()).ShouldBeFalse();

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        patient.Id.ShouldBeGreaterThan(0);
        context.ChangeTracker.Clear();
        (await context.Patients.SingleAsync(value => value.Id == patient.Id)).UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task SaveChangesAsync_traduce_el_perfil_duplicado_con_su_excepcion_original()
    {
        var userId = await SaveUserAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var (_, repository, unitOfWork) = Resolve(scope.ServiceProvider);
        repository.Add(PatientMother.WithRequiredData(userId));
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        repository.Add(PatientMother.WithRequiredData(userId));

        var conflict = await Should.ThrowAsync<PersistenceConflictException>(() =>
            unitOfWork.SaveChangesAsync(CancellationToken.None));

        conflict.Error.ShouldBe(PatientErrors.ProfileAlreadyExists);
        var update = conflict.InnerException.ShouldBeOfType<DbUpdateException>();
        var postgres = update.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("uq_patient_user_id");
        postgres.SchemaName.ShouldBe("people");
        postgres.TableName.ShouldBe("patient");
    }

    [Fact]
    public async Task AddInfrastructure_registra_el_repositorio_por_scope()
    {
        await using var provider = CreateProvider();
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var repository = first.ServiceProvider.GetRequiredService<IPatientRepository>();

        repository.ShouldBeOfType<PatientRepository>();
        repository.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IPatientRepository>());
        repository.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IPatientRepository>());
    }

    private async Task<int> SaveUserAsync(string? email = null)
    {
        var user = UserMother.Confirmed(email is null ? null : Email.Create(email).Value);
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options => options.SgcmDatabase = Database.ConnectionString);
        services.AddInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static (AppDbContext Context, IPatientRepository Repository, IUnitOfWork UnitOfWork) Resolve(
        IServiceProvider services) =>
        (services.GetRequiredService<AppDbContext>(), services.GetRequiredService<IPatientRepository>(),
            services.GetRequiredService<IUnitOfWork>());
}
