using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Data.Infrastructure.Persistence;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public class InsuranceProviderMappingTests
{
    private static readonly StoreObjectIdentifier InsuranceProviderTable =
        StoreObjectIdentifier.Table("insurance_provider", "people");

    [Fact]
    public void InsuranceProvider_se_mapea_a_su_tabla_y_clave_con_identidad_always()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(InsuranceProvider))!;

        entity.GetTableName().ShouldBe("insurance_provider");
        entity.GetSchema().ShouldBe("people");
        entity.FindPrimaryKey()!.GetName().ShouldBe("pk_insurance_provider");
        entity.FindPrimaryKey()!.Properties.Single().Name.ShouldBe(nameof(InsuranceProvider.Id));
        var id = entity.FindProperty(nameof(InsuranceProvider.Id))!;
        id.ValueGenerated.ShouldBe(ValueGenerated.OnAdd);
        id.GetValueGenerationStrategy().ShouldBe(NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);
    }

    [Theory]
    [InlineData(nameof(InsuranceProvider.Id), "id", "integer", null, false)]
    [InlineData(nameof(InsuranceProvider.Name), "name", "character varying(150)", 150, false)]
    [InlineData(nameof(InsuranceProvider.ContactPhone), "contact_phone", "character varying(20)", 20, false)]
    [InlineData(nameof(InsuranceProvider.ContactEmail), "contact_email", "character varying(256)", 256, false)]
    [InlineData(nameof(InsuranceProvider.Address), "address", "character varying(250)", 250, false)]
    [InlineData(nameof(InsuranceProvider.Website), "website", "character varying(250)", 250, true)]
    [InlineData(nameof(InsuranceProvider.IsActive), "is_active", "boolean", null, false)]
    public void InsuranceProvider_respeta_columnas_tipos_longitudes_y_obligatoriedad(
        string propertyName, string columnName, string columnType, int? maxLength, bool isNullable)
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(InsuranceProvider))!.FindProperty(propertyName)!;

        property.GetColumnName(InsuranceProviderTable).ShouldBe(columnName);
        property.GetColumnType().ShouldBe(columnType);
        property.GetMaxLength().ShouldBe(maxLength);
        property.IsNullable.ShouldBe(isNullable);
    }

    [Fact]
    public void ContactEmail_se_convierte_a_texto_y_vuelve_al_objeto_de_valor()
    {
        using var context = CreateContext();
        var converter = context.Model.FindEntityType(typeof(InsuranceProvider))!
            .FindProperty(nameof(InsuranceProvider.ContactEmail))!
            .GetValueConverter()!;
        var email = Email.Create("contacto@seguros.com").Value;

        converter.ProviderClrType.ShouldBe(typeof(string));
        converter.ConvertToProvider(email).ShouldBe("contacto@seguros.com");
        converter.ConvertFromProvider("contacto@seguros.com").ShouldBe(email);
    }

    [Fact]
    public void InsuranceProvider_solo_persiste_sus_datos_y_envia_el_estado_explicitamente()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(InsuranceProvider))!;

        entity.GetProperties().Select(property => property.Name).ShouldBe(new[]
        {
            "Id", "Name", "ContactPhone", "ContactEmail", "Address", "Website", "IsActive"
        }, ignoreOrder: true);
        entity.FindNavigation(nameof(InsuranceProvider.DomainEvents)).ShouldBeNull();
        entity.FindProperty(nameof(InsuranceProvider.IsActive))!.ValueGenerated.ShouldBe(ValueGenerated.Never);
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql()
            .UseSnakeCaseNamingConvention()
            .Options);
}
