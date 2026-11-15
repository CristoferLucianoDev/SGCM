using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public class InsuranceProviderRepositoryTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const string OtherName = "Seguros Reservas";

    [Fact]
    public async Task GetByIdAsync_devuelve_el_proveedor_rastreado_y_UnitOfWork_guarda_sus_cambios()
    {
        await using var provider = CreateProvider();
        var id = await SaveAsync(provider, InsuranceProviderMother.Valid());

        await using (var scope = provider.CreateAsyncScope())
        {
            var (_, repository, unitOfWork) = Resolve(scope.ServiceProvider);
            var insuranceProvider = await repository.GetByIdAsync(id, CancellationToken.None);

            insuranceProvider.ShouldNotBeNull();
            insuranceProvider.Deactivate().IsSuccess.ShouldBeTrue();
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var check = provider.CreateAsyncScope();
        var (checkContext, _, _) = Resolve(check.ServiceProvider);
        (await checkContext.InsuranceProviders.AsNoTracking().SingleAsync(value => value.Id == id))
            .IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_con_id_inexistente_devuelve_null()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var (_, repository, _) = Resolve(scope.ServiceProvider);

        (await repository.GetByIdAsync(int.MaxValue, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task ExistsByNameAsync_con_el_mismo_nombre_devuelve_true()
    {
        await using var provider = CreateProvider();
        await SaveAsync(provider, InsuranceProviderMother.Valid());

        (await ExistsByNameAsync(provider, InsuranceProviderMother.DefaultName)).ShouldBeTrue();
    }

    [Fact]
    public async Task ExistsByNameAsync_ignora_los_espacios_en_los_extremos()
    {
        await using var provider = CreateProvider();
        await SaveAsync(provider, InsuranceProviderMother.Valid());

        (await ExistsByNameAsync(provider, $"  {InsuranceProviderMother.DefaultName}  ")).ShouldBeTrue();
    }

    [Fact]
    public async Task ExistsByNameAsync_distingue_mayusculas_como_la_restriccion_de_la_base()
    {
        await using var provider = CreateProvider();
        await SaveAsync(provider, InsuranceProviderMother.Valid());

        (await ExistsByNameAsync(provider, InsuranceProviderMother.DefaultName.ToUpperInvariant())).ShouldBeFalse();
    }

    [Fact]
    public async Task ExistsByNameAsync_cuenta_el_proveedor_inactivo()
    {
        await using var provider = CreateProvider();
        var inactive = InsuranceProviderMother.Valid();
        inactive.Deactivate().IsSuccess.ShouldBeTrue();
        await SaveAsync(provider, inactive);

        (await ExistsByNameAsync(provider, InsuranceProviderMother.DefaultName)).ShouldBeTrue();
    }

    [Fact]
    public async Task ExistsByNameAsync_excluye_el_proveedor_que_se_actualiza()
    {
        await using var provider = CreateProvider();
        var id = await SaveAsync(provider, InsuranceProviderMother.Valid());

        (await ExistsByNameAsync(provider, InsuranceProviderMother.DefaultName, excludeId: id)).ShouldBeFalse();
    }

    [Fact]
    public async Task ExistsByNameAsync_con_excludeId_de_otro_proveedor_devuelve_true()
    {
        await using var provider = CreateProvider();
        await SaveAsync(provider, InsuranceProviderMother.Valid());
        var otherId = await SaveAsync(provider, InsuranceProviderMother.Valid(OtherName));

        (await ExistsByNameAsync(provider, InsuranceProviderMother.DefaultName, excludeId: otherId)).ShouldBeTrue();
    }

    [Fact]
    public async Task ExistsByNameAsync_sin_proveedores_devuelve_false()
    {
        await using var provider = CreateProvider();

        (await ExistsByNameAsync(provider, InsuranceProviderMother.DefaultName)).ShouldBeFalse();
    }

    private static async Task<int> SaveAsync(ServiceProvider provider, InsuranceProvider insuranceProvider)
    {
        await using var scope = provider.CreateAsyncScope();
        var (_, repository, unitOfWork) = Resolve(scope.ServiceProvider);
        repository.Add(insuranceProvider);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        return insuranceProvider.Id;
    }

    private static async Task<bool> ExistsByNameAsync(ServiceProvider provider, string name, int? excludeId = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var (_, repository, _) = Resolve(scope.ServiceProvider);
        return await repository.ExistsByNameAsync(name, excludeId);
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options => options.SgcmDatabase = Database.ConnectionString);
        services.AddInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static (AppDbContext Context, IInsuranceProviderRepository Repository, IUnitOfWork UnitOfWork) Resolve(
        IServiceProvider services) =>
        (services.GetRequiredService<AppDbContext>(), services.GetRequiredService<IInsuranceProviderRepository>(),
            services.GetRequiredService<IUnitOfWork>());
}
