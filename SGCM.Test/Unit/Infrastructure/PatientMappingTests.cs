using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Infrastructure.Persistence;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public class PatientMappingTests
{
    private static readonly StoreObjectIdentifier PatientTable = StoreObjectIdentifier.Table("patient", "people");

    [Fact]
    public void Patient_se_mapea_a_su_tabla_y_clave_con_identidad_always()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Patient))!;

        entity.GetTableName().ShouldBe("patient");
        entity.GetSchema().ShouldBe("people");
        entity.FindPrimaryKey()!.GetName().ShouldBe("pk_patient");
        entity.FindPrimaryKey()!.Properties.Single().Name.ShouldBe(nameof(Patient.Id));
        var id = entity.FindProperty(nameof(Patient.Id))!;
        id.ValueGenerated.ShouldBe(ValueGenerated.OnAdd);
        id.GetValueGenerationStrategy().ShouldBe(NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);
    }

    [Theory]
    [InlineData(nameof(Patient.Id), "id", "integer", null, false)]
    [InlineData(nameof(Patient.UserId), "user_id", "integer", null, false)]
    [InlineData(nameof(Patient.FirstName), "first_name", "character varying(100)", 100, false)]
    [InlineData(nameof(Patient.LastName), "last_name", "character varying(100)", 100, false)]
    [InlineData(nameof(Patient.BirthDate), "birth_date", "date", null, false)]
    [InlineData(nameof(Patient.PhoneNumber), "phone_number", "character varying(20)", 20, false)]
    [InlineData(nameof(Patient.Address), "address", "character varying(250)", 250, true)]
    [InlineData(nameof(Patient.InsuranceProviderId), "insurance_provider_id", "integer", null, true)]
    [InlineData(nameof(Patient.PolicyNumber), "policy_number", "character varying(50)", 50, true)]
    [InlineData(nameof(Patient.IsActive), "is_active", "boolean", null, false)]
    [InlineData(nameof(Patient.CreatedAt), "created_at", "timestamp with time zone", null, false)]
    [InlineData(nameof(Patient.CreatedBy), "created_by", "integer", null, true)]
    [InlineData(nameof(Patient.UpdatedAt), "updated_at", "timestamp with time zone", null, true)]
    [InlineData(nameof(Patient.UpdatedBy), "updated_by", "integer", null, true)]
    public void Patient_respeta_columnas_tipos_longitudes_y_obligatoriedad(
        string propertyName, string columnName, string columnType, int? maxLength, bool isNullable)
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(Patient))!.FindProperty(propertyName)!;

        property.GetColumnName(PatientTable).ShouldBe(columnName);
        property.GetColumnType().ShouldBe(columnType);
        property.GetMaxLength().ShouldBe(maxLength);
        property.IsNullable.ShouldBe(isNullable);
    }

    [Theory]
    [InlineData(nameof(EmergencyContact.Name), "emergency_contact_name", "character varying(150)", 150)]
    [InlineData(nameof(EmergencyContact.PhoneNumber), "emergency_contact_phone", "character varying(20)", 20)]
    public void EmergencyContact_comparte_la_tabla_del_paciente_con_columnas_opcionales(
        string propertyName, string columnName, string columnType, int maxLength)
    {
        using var context = CreateContext();
        var contact = context.Model.FindEntityType(typeof(EmergencyContact))!;
        var property = contact.FindProperty(propertyName)!;

        contact.GetTableName().ShouldBe("patient");
        contact.GetSchema().ShouldBe("people");
        property.GetColumnName(PatientTable).ShouldBe(columnName);
        property.GetColumnType().ShouldBe(columnType);
        property.GetMaxLength().ShouldBe(maxLength);
        // El contacto es opcional: sus columnas aceptan NULL aunque, si existe, ambos datos son obligatorios.
        property.IsNullable.ShouldBeFalse();
        property.IsColumnNullable(PatientTable).ShouldBeTrue();
    }

    [Fact]
    public void EmergencyContact_es_un_objeto_de_valor_opcional_del_paciente()
    {
        using var context = CreateContext();
        var navigation = context.Model.FindEntityType(typeof(Patient))!
            .FindNavigation(nameof(Patient.EmergencyContact))!;

        navigation.TargetEntityType.IsOwned().ShouldBeTrue();
        navigation.ForeignKey.IsRequiredDependent.ShouldBeFalse();
    }

    [Fact]
    public void Patient_solo_persiste_sus_datos_y_envia_el_estado_explicitamente()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Patient))!;

        entity.GetProperties().Select(property => property.Name).ShouldBe(new[]
        {
            "Id", "UserId", "FirstName", "LastName", "BirthDate", "PhoneNumber", "Address",
            "InsuranceProviderId", "PolicyNumber", "IsActive", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"
        }, ignoreOrder: true);
        entity.FindNavigation(nameof(Patient.DomainEvents)).ShouldBeNull();
        entity.FindProperty(nameof(Patient.IsActive))!.ValueGenerated.ShouldBe(ValueGenerated.Never);
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql()
            .UseSnakeCaseNamingConvention()
            .Options);
}
