using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Data.Infrastructure.Persistence.Repositories.Identity;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public class TwoFactorStatusReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task IsEnabledAsync_sin_fila_devuelve_false()
    {
        var userId = await SaveAsync(UserMother.Confirmed());

        (await IsEnabledAsync(userId)).ShouldBeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_con_el_segundo_factor_inactivo_devuelve_false()
    {
        var userId = await SaveWithTwoFactorAsync(isEnabled: false);

        (await IsEnabledAsync(userId)).ShouldBeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_con_el_segundo_factor_activo_devuelve_true()
    {
        var userId = await SaveWithTwoFactorAsync(isEnabled: true);

        (await IsEnabledAsync(userId)).ShouldBeTrue();
    }

    private async Task<bool> IsEnabledAsync(int userId)
    {
        await using var context = CreateContext();
        return await new TwoFactorStatusReader(context).IsEnabledAsync(userId, CancellationToken.None);
    }

    private async Task<int> SaveWithTwoFactorAsync(bool isEnabled)
    {
        var userId = await SaveAsync(UserMother.Confirmed());

        await using var context = CreateContext();
        await TestDataSeeder.EnableTwoFactorAsync(context, userId, isEnabled);

        return userId;
    }

    private async Task<int> SaveAsync(User user)
    {
        await using var context = CreateContext();
        new UserRepository(context).Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}
