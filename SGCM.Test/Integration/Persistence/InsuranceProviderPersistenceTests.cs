using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public class InsuranceProviderPersistenceTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task InsuranceProvider_persiste_y_recupera_todos_sus_datos()
    {
        var loaded = await SaveAndReloadAsync(InsuranceProviderMother.Valid());

        loaded.Name.ShouldBe(InsuranceProviderMother.DefaultName);
        loaded.ContactPhone.ShouldBe(InsuranceProviderMother.DefaultContactPhone);
        loaded.ContactEmail.Value.ShouldBe(InsuranceProviderMother.DefaultContactEmail);
        loaded.Address.ShouldBe(InsuranceProviderMother.DefaultAddress);
        loaded.Website.ShouldBe(InsuranceProviderMother.DefaultWebsite);
        loaded.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task InsuranceProvider_sin_sitio_web_recupera_el_sitio_como_null()
    {
        var loaded = await SaveAndReloadAsync(InsuranceProviderMother.Valid(website: null));

        loaded.Website.ShouldBeNull();
    }

    [Fact]
    public async Task InsuranceProvider_inactivo_persiste_el_estado_explicito()
    {
        var provider = InsuranceProviderMother.Valid();
        provider.Deactivate().IsSuccess.ShouldBeTrue();

        var loaded = await SaveAndReloadAsync(provider);

        loaded.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task UnitOfWork_traduce_el_nombre_duplicado_con_su_excepcion_original()
    {
        await SaveAndReloadAsync(InsuranceProviderMother.Valid());
        await using var services = CreateProvider();
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IInsuranceProviderRepository>().Add(InsuranceProviderMother.Valid());

        var conflict = await Should.ThrowAsync<PersistenceConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));

        conflict.Error.ShouldBe(InsuranceProviderErrors.NameAlreadyInUse);
        var update = conflict.InnerException.ShouldBeOfType<DbUpdateException>();
        var postgres = update.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("uq_insurance_provider_name");
    }

    private async Task<InsuranceProvider> SaveAndReloadAsync(InsuranceProvider provider)
    {
        await using (var context = CreateContext())
        {
            context.InsuranceProviders.Add(provider);
            await context.SaveChangesAsync();
        }

        provider.Id.ShouldBeGreaterThan(0);

        await using var reloadContext = CreateContext();
        return await reloadContext.InsuranceProviders.SingleAsync(value => value.Id == provider.Id);
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options => options.SgcmDatabase = Database.ConnectionString);
        services.AddInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}
