using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
[Collection(DatabaseCollection.Name)]
public class TextNormalizationTests(PostgresFixture database)
{
    [Theory]
    [InlineData("CARDIOLOGÍA")]
    [InlineData("cardiologia")]
    [InlineData("  CARDIOLOGIA  ")]
    public async Task NormalizeText_compara_sin_mayusculas_ni_tildes_y_conserva_el_nombre(string search)
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var specialty = Specialty.Create("Cardiología", "Atención del corazón.").Value;
        context.Specialties.Add(specialty);
        await context.SaveChangesAsync();

        var names = await context.Specialties
            .Where(value => value.Id == specialty.Id &&
                PostgresFunctions.NormalizeText(value.Name) == PostgresFunctions.NormalizeText(search))
            .Select(value => value.Name)
            .ToListAsync();

        names.ShouldBe(new[] { "Cardiología" });
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("  ATENCIÓN A LA NIÑEZ  ", "atencion a la ninez")]
    [InlineData("medicina  INTERNA", "medicina  interna")]
    [InlineData("\tCARDIOLOGÍA\t", "\tcardiologia\t")]
    public async Task NormalizeText_respeta_el_comportamiento_SQL(string? input, string? expected)
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var specialty = Specialty.Create("Cardiología", "Atención del corazón.").Value;
        context.Specialties.Add(specialty);
        await context.SaveChangesAsync();

        var normalized = await context.Specialties
            .Where(value => value.Id == specialty.Id)
            .Select(value => PostgresFunctions.NormalizeText(input))
            .SingleAsync();

        normalized.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Neurología")]
    [InlineData(null)]
    public async Task NormalizeText_no_confunde_un_nombre_con_otro_texto_o_con_null(string? search)
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var specialty = Specialty.Create("Cardiología", "Atención del corazón.").Value;
        context.Specialties.Add(specialty);
        await context.SaveChangesAsync();

        var exists = await context.Specialties.AnyAsync(value => value.Id == specialty.Id &&
            PostgresFunctions.NormalizeText(value.Name) == PostgresFunctions.NormalizeText(search));

        exists.ShouldBeFalse();
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}
