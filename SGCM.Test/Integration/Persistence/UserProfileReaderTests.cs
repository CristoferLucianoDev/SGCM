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
public class UserProfileReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private static readonly DateOnly BirthDate = new(1990, 5, 20);

    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Administrator)]
    public async Task HasProfileAsync_sin_perfil_devuelve_false(UserRole role)
    {
        var userId = await SaveAsync(UserMother.WithRole(role));

        (await HasProfileAsync(userId, role)).ShouldBeFalse();
    }

    [Fact]
    public async Task HasProfileAsync_con_perfil_de_paciente_devuelve_true()
    {
        var userId = await SavePatientWithProfileAsync();

        (await HasProfileAsync(userId, UserRole.Patient)).ShouldBeTrue();
    }

    [Fact]
    public async Task HasProfileAsync_no_confunde_perfiles_de_otro_rol()
    {
        var userId = await SavePatientWithProfileAsync();

        (await HasProfileAsync(userId, UserRole.Doctor)).ShouldBeFalse();
    }

    private async Task<bool> HasProfileAsync(int userId, UserRole role)
    {
        await using var context = CreateContext();
        return await new UserProfileReader(context).HasProfileAsync(userId, role, CancellationToken.None);
    }

    // Solo las columnas obligatorias; el trigger de rol exige que la cuenta sea de paciente.
    private async Task<int> SavePatientWithProfileAsync()
    {
        var userId = await SaveAsync(UserMother.Confirmed());

        await using var context = CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO people.patient (user_id, first_name, last_name, birth_date, phone_number)
            VALUES ({userId}, 'Ana', 'Pérez', {BirthDate}, '8095550100')
            """);

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
