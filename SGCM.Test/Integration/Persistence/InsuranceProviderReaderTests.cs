using Microsoft.EntityFrameworkCore;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public class InsuranceProviderReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task IsActiveAsync_con_proveedor_activo_devuelve_true()
    {
        var insuranceProviderId = await InsertInsuranceProviderAsync();

        (await IsActiveAsync(insuranceProviderId)).ShouldBeTrue();
    }

    [Fact]
    public async Task IsActiveAsync_con_proveedor_inactivo_devuelve_false()
    {
        var insuranceProviderId = await InsertInsuranceProviderAsync();
        await using (var context = CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE people.insurance_provider SET is_active = false WHERE id = {insuranceProviderId}");
        }

        (await IsActiveAsync(insuranceProviderId)).ShouldBeFalse();
    }

    [Fact]
    public async Task IsActiveAsync_con_proveedor_inexistente_devuelve_false()
    {
        (await IsActiveAsync(int.MaxValue)).ShouldBeFalse();
    }

    private async Task<bool> IsActiveAsync(int insuranceProviderId)
    {
        await using var context = CreateContext();
        return await new InsuranceProviderReader(context).IsActiveAsync(insuranceProviderId, CancellationToken.None);
    }

    private async Task<int> InsertInsuranceProviderAsync()
    {
        await using var context = CreateContext();
        return await TestDataSeeder.InsertInsuranceProviderAsync(context);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}
