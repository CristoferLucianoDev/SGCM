using Microsoft.EntityFrameworkCore;
using Npgsql;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
[Collection(DatabaseCollection.Name)]
public class SpecialtyPersistenceTests(PostgresFixture database)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Specialty_persiste_y_recupera_el_estado_explicito(bool isActive)
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var specialty = Specialty.Create("CARDIOLOGÍA", "Atención  del corazón.").Value;
        if (!isActive)
        {
            specialty.Deactivate().IsSuccess.ShouldBeTrue();
        }

        context.Specialties.Add(specialty);
        await context.SaveChangesAsync();
        specialty.Id.ShouldBeGreaterThan(0);
        context.ChangeTracker.Clear();

        var loaded = await context.Specialties.SingleAsync(value => value.Id == specialty.Id);

        loaded.Name.ShouldBe("Cardiología");
        loaded.Description.ShouldBe("Atención  del corazón.");
        loaded.IsActive.ShouldBe(isActive);
    }

    [Fact]
    public async Task Specialty_persiste_las_longitudes_maximas()
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var specialty = Specialty.Create(new string('a', 100), new string('b', 500)).Value;
        context.Specialties.Add(specialty);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var loaded = await context.Specialties.SingleAsync(value => value.Id == specialty.Id);

        loaded.Name.ShouldBe("A" + new string('a', 99));
        loaded.Description.ShouldBe(new string('b', 500));
    }

    [Theory]
    [InlineData("CARDIOLOGÍA")]
    [InlineData("cardiologia")]
    [InlineData("  Cardiología  ")]
    public async Task El_indice_unico_rechaza_nombres_equivalentes(string duplicateName)
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Specialties.Add(Specialty.Create("Cardiología", "Atención del corazón.").Value);
        await context.SaveChangesAsync();

        // Se conserva la entrada original para probar el índice sin la normalización del dominio.
        var exception = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO people.specialty (name, description) VALUES ({duplicateName}, {"Otra descripción."})"));

        exception.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        exception.ConstraintName.ShouldBe("uq_specialty_name");
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}
