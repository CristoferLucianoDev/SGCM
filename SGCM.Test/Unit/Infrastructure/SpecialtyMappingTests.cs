using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public class SpecialtyMappingTests
{
    [Fact]
    public void Specialty_se_mapea_a_su_tabla_y_clave_con_identidad_always()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Specialty))!;

        entity.GetTableName().ShouldBe("specialty");
        entity.GetSchema().ShouldBe("people");
        entity.FindPrimaryKey()!.GetName().ShouldBe("pk_specialty");
        entity.FindPrimaryKey()!.Properties.Single().Name.ShouldBe(nameof(Specialty.Id));
        var id = entity.FindProperty(nameof(Specialty.Id))!;
        id.ValueGenerated.ShouldBe(ValueGenerated.OnAdd);
        id.GetValueGenerationStrategy().ShouldBe(NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);
    }

    [Theory]
    [InlineData(nameof(Specialty.Id), "id", "integer", null)]
    [InlineData(nameof(Specialty.Name), "name", "character varying(100)", 100)]
    [InlineData(nameof(Specialty.Description), "description", "character varying(500)", 500)]
    [InlineData(nameof(Specialty.IsActive), "is_active", "boolean", null)]
    public void Specialty_respeta_columnas_tipos_longitudes_y_obligatoriedad(
        string propertyName, string columnName, string columnType, int? maxLength)
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(Specialty))!.FindProperty(propertyName)!;

        property.GetColumnName(StoreObjectIdentifier.Table("specialty", "people")).ShouldBe(columnName);
        property.GetColumnType().ShouldBe(columnType);
        property.GetMaxLength().ShouldBe(maxLength);
        property.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void Specialty_solo_persiste_sus_datos_y_envia_el_estado_explicitamente()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Specialty))!;

        entity.GetProperties().Select(property => property.Name)
            .ShouldBe(new[] { "Id", "Name", "Description", "IsActive" }, ignoreOrder: true);
        entity.FindNavigation(nameof(Specialty.DomainEvents)).ShouldBeNull();
        entity.FindProperty(nameof(Specialty.IsActive))!.ValueGenerated.ShouldBe(ValueGenerated.Never);
    }

    [Fact]
    public void NormalizeText_registra_la_firma_SQL_y_la_propagacion_de_nulos()
    {
        using var context = CreateContext();
        var method = typeof(PostgresFunctions).GetMethod(nameof(PostgresFunctions.NormalizeText))!;
        var function = context.Model.FindDbFunction(method)!;

        function.Name.ShouldBe("sp_text_normalize");
        function.Schema.ShouldBe("people");
        function.StoreType.ShouldBe("text");
        function.IsNullable.ShouldBeTrue();
        var parameter = function.Parameters.Single();
        parameter.ClrType.ShouldBe(typeof(string));
        parameter.StoreType.ShouldBe("text");
        parameter.PropagatesNullability.ShouldBeTrue();
    }

    [Fact]
    public void NormalizeText_se_traduce_en_el_filtro_para_la_columna_y_el_parametro()
    {
        using var context = CreateContext();
        var search = "CARDIOLOGIA";

        var sql = context.Specialties
            .Where(specialty => PostgresFunctions.NormalizeText(specialty.Name) == PostgresFunctions.NormalizeText(search))
            .ToQueryString();

        sql.ShouldContain("FROM people.specialty");
        sql.ShouldContain("WHERE");
        Regex.Matches(sql, @"people\.sp_text_normalize\(").Count.ShouldBe(2);
        sql.ShouldContain("@__search");
    }

    [Fact]
    public void NormalizeText_no_ofrece_una_normalizacion_alternativa_en_memoria()
    {
        Should.Throw<NotSupportedException>(() => PostgresFunctions.NormalizeText("Cardiología"));
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql()
            .UseSnakeCaseNamingConvention()
            .Options);
}
