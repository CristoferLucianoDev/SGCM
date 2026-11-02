using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public class SpecialtyUpdatePersistenceTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const string Name = "Neurología";
    private const string Description = "Atención neurológica.";
    private readonly HashSet<int> _ids = [];

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public async Task Update_persiste_solo_campos_presentes_y_conserva_identidad_y_estado(
        bool nameProvided, bool descriptionProvided, bool isActive)
    {
        var original = await SeedAsync(isActive: isActive);
        await using var provider = CreateProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);
            var specialty = (await repository.GetByIdAsync(original.Id, CancellationToken.None))!;

            specialty.Update(nameProvided, "  ATENCIÓN A LA NIÑEZ  ", descriptionProvided, "  Atención  MÉDICA.  ")
                .IsSuccess.ShouldBeTrue();
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        await AssertSavedAsync(original.Id, nameProvided ? "Atención A La Niñez" : Name,
            descriptionProvided ? "Atención  MÉDICA." : Description, isActive);
    }

    [Fact]
    public async Task Update_persiste_las_longitudes_maximas()
    {
        var original = await SeedAsync();
        await using (var context = CreateContext())
        {
            var specialty = await context.Specialties.SingleAsync(value => value.Id == original.Id);
            specialty.Update(true, "  " + new string('a', 100) + "  ", true, "  " + new string('b', 500) + "  ")
                .IsSuccess.ShouldBeTrue();
            await context.SaveChangesAsync();
        }

        await AssertSavedAsync(original.Id, "A" + new string('a', 99), new string('b', 500), true);
    }

    [Theory]
    [InlineData("Cardiología", "Cardiología")]
    [InlineData("cardiologia", "Cardiologia")]
    [InlineData("CARDIOLOGÍA", "Cardiología")]
    [InlineData("CARDIOLOGIA", "Cardiologia")]
    public async Task Update_del_propio_id_no_colisiona_con_el_indice_unico(string name, string expected)
    {
        var original = await SeedAsync("Cardiología", false);
        await using var provider = CreateProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>();
            var specialty = (await repository.GetByIdAsync(original.Id, CancellationToken.None))!;
            (await repository.ExistsByNameAsync(name, original.Id)).ShouldBeFalse();
            (await repository.ExistsByNameAsync(name)).ShouldBeTrue();
            specialty.Update(true, name, true, "Nueva descripción.").IsSuccess.ShouldBeTrue();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await AssertSavedAsync(original.Id, expected, "Nueva descripción.", false);
    }

    [Theory]
    [InlineData("Cardiología")]
    [InlineData("cardiologia")]
    [InlineData("CARDIOLOGÍA")]
    [InlineData("CARDIOLOGIA")]
    public async Task Indice_PostgreSQL_rechaza_UPDATE_duplicado_y_no_persiste_ningun_campo(string duplicate)
    {
        var original = await SeedAsync(isActive: false);
        var existing = await SeedAsync("Cardiología");
        await using var provider = CreateProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);
            var specialty = (await repository.GetByIdAsync(original.Id, CancellationToken.None))!;
            (await repository.ExistsByNameAsync(duplicate, original.Id)).ShouldBeTrue();
            specialty.Update(true, duplicate, true, "No debe guardarse.").IsSuccess.ShouldBeTrue();

            // Conserva la entrada sin normalizar para probar el índice, incluso sus mayúsculas.
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Entry(specialty).Property(value => value.Name).CurrentValue = duplicate;
            context.ChangeTracker.DetectChanges();
            context.Entry(specialty).State.ShouldBe(EntityState.Modified);
            specialty.Name.ShouldBe(duplicate);

            var conflict = await Should.ThrowAsync<PersistenceConflictException>(() =>
                unitOfWork.SaveChangesAsync(CancellationToken.None));

            conflict.Error.ShouldBe(SpecialtyErrors.NameAlreadyInUse);
            var update = conflict.InnerException.ShouldBeOfType<DbUpdateException>();
            var postgres = update.InnerException.ShouldBeOfType<PostgresException>();
            postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            postgres.SchemaName.ShouldBe("people");
            postgres.TableName.ShouldBe("specialty");
            postgres.ConstraintName.ShouldBe("uq_specialty_name");
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await AssertOriginalAsync(original);
        await AssertOriginalAsync(existing);
    }

    public static IEnumerable<object?[]> InvalidUpdates()
    {
        yield return [false, null, false, null, "Specialty.NoFieldsProvided"];
        foreach (var invalid in new string?[] { null, "", "   " })
        {
            yield return [true, invalid, true, "Descripción nueva.", "Specialty.NameRequired"];
            yield return [true, "Pediatría", true, invalid, "Specialty.DescriptionRequired"];
        }
        yield return [true, new string('a', 101), true, "Descripción nueva.", "Specialty.NameTooLong"];
        yield return [true, "Pediatría", true, new string('b', 501), "Specialty.DescriptionTooLong"];
    }

    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public async Task Update_invalido_no_altera_la_entidad_ni_la_base_aun_si_se_guarda(
        bool nameProvided, string? name, bool descriptionProvided, string? description, string code)
    {
        var original = await SeedAsync(isActive: false);
        await using (var context = CreateContext())
        {
            var specialty = await context.Specialties.SingleAsync(value => value.Id == original.Id);

            var result = specialty.Update(nameProvided, name, descriptionProvided, description);

            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe(code);
            context.ChangeTracker.DetectChanges();
            context.Entry(specialty).State.ShouldBe(EntityState.Unchanged);
            (await context.SaveChangesAsync()).ShouldBe(0);
        }

        await AssertOriginalAsync(original);
    }

    [Fact]
    public async Task Rollback_revierte_un_UPDATE_ya_enviado_a_PostgreSQL()
    {
        var original = await SeedAsync(isActive: false);
        await using var provider = CreateProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);
            var specialty = (await repository.GetByIdAsync(original.Id, CancellationToken.None))!;
            specialty.Update(true, "Pediatría", true, "Cambio provisional.").IsSuccess.ShouldBeTrue();
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await context.Specialties.AsNoTracking().SingleAsync(value => value.Id == original.Id)).Name
                .ShouldBe("Pediatría");

            await transaction.RollbackAsync(CancellationToken.None);
        }

        await AssertOriginalAsync(original);
    }

    public override async Task DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var context = CreateContext();
            await context.Specialties.Where(value => _ids.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
            (await context.Specialties.AnyAsync(value => _ids.Contains(value.Id), timeout.Token)).ShouldBeFalse();
        }
        finally
        {
            await base.DisposeAsync();
        }
    }

    private async Task<Specialty> SeedAsync(string name = Name, bool isActive = true)
    {
        var specialty = Specialty.Create(name, Description).Value;
        if (!isActive) specialty.Deactivate().IsSuccess.ShouldBeTrue();
        await using var context = CreateContext();
        context.Specialties.Add(specialty);
        await context.SaveChangesAsync();
        _ids.Add(specialty.Id);
        return specialty;
    }

    private Task AssertOriginalAsync(Specialty specialty) =>
        AssertSavedAsync(specialty.Id, specialty.Name, specialty.Description, specialty.IsActive);

    private async Task AssertSavedAsync(int id, string name, string description, bool isActive)
    {
        await using var context = CreateContext();
        var saved = await context.Specialties.AsNoTracking().SingleAsync(value => value.Id == id);
        saved.Id.ShouldBe(id);
        saved.Name.ShouldBe(name);
        saved.Description.ShouldBe(description);
        saved.IsActive.ShouldBe(isActive);
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(Database.ConnectionString).UseSnakeCaseNamingConvention().Options);

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options => options.SgcmDatabase = Database.ConnectionString);
        services.AddInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
