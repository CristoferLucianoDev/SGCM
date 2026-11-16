using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class DoctorMappingTests
{
    [Fact]
    public void Doctor_respeta_identidad_indices_y_relacion_con_User()
    {
        using var context = Context();
        var entity = context.Model.FindEntityType(typeof(Doctor))!;
        entity.GetTableName().ShouldBe("doctor");
        entity.GetSchema().ShouldBe("people");
        entity.FindPrimaryKey()!.GetName().ShouldBe("pk_doctor");
        entity.FindPrimaryKey()!.Properties.Select(value => value.Name).ShouldBe(new[] { "Id" });
        entity.FindProperty("Id")!.GetValueGenerationStrategy().ShouldBe(NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);
        entity.GetIndexes().Single(index => index.GetDatabaseName() == "uq_doctor_user_id").IsUnique.ShouldBeTrue();
        entity.GetIndexes().Single(index => index.GetDatabaseName() == "uq_doctor_license_number").IsUnique.ShouldBeTrue();
        var user = entity.GetForeignKeys().Single(key => key.PrincipalEntityType.ClrType == typeof(User));
        user.Properties.Single().Name.ShouldBe(nameof(Doctor.UserId));
        user.GetConstraintName().ShouldBe("fk_doctor_user");
        entity.FindProperty(nameof(Doctor.IsActive))!.ValueGenerated.ShouldBe(ValueGenerated.Never);
        entity.FindNavigation(nameof(Doctor.DomainEvents)).ShouldBeNull();
        entity.GetProperties().Select(value => value.Name).ShouldBe(new[]
        {
            "Id", "UserId", "FirstName", "LastName", "LicenseNumber", "PhoneNumber", "ProvinceId",
            "MunicipalityId", "AddressLine", "ConsultationFee", "YearsOfExperience", "Biography", "IsActive"
        }, ignoreOrder: true);
    }

    [Theory]
    [InlineData("Id", "id", "integer", null)]
    [InlineData("UserId", "user_id", "integer", null)]
    [InlineData("FirstName", "first_name", "character varying(100)", 100)]
    [InlineData("LastName", "last_name", "character varying(100)", 100)]
    [InlineData("LicenseNumber", "license_number", "character varying(50)", 50)]
    [InlineData("PhoneNumber", "phone_number", "character varying(20)", 20)]
    [InlineData("ProvinceId", "province_id", "integer", null)]
    [InlineData("MunicipalityId", "municipality_id", "integer", null)]
    [InlineData("AddressLine", "address_line", "character varying(250)", 250)]
    [InlineData("ConsultationFee", "consultation_fee", "numeric(10,2)", null)]
    [InlineData("YearsOfExperience", "years_of_experience", "integer", null)]
    [InlineData("Biography", "biography", "character varying(1000)", 1000)]
    [InlineData("IsActive", "is_active", "boolean", null)]
    public void Doctor_respeta_columnas_tipos_longitudes_y_campos_obligatorios(
        string name, string column, string type, int? length)
    {
        using var context = Context();
        var property = context.Model.FindEntityType(typeof(Doctor))!.FindProperty(name)!;
        property.GetColumnName(StoreObjectIdentifier.Table("doctor", "people")).ShouldBe(column);
        property.GetColumnType().ShouldBe(type);
        property.GetMaxLength().ShouldBe(length);
        property.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void Doctor_mapea_las_claves_geograficas_en_el_orden_del_esquema()
    {
        using var context = Context();
        var doctor = context.Model.FindEntityType(typeof(Doctor))!;
        var province = doctor.GetForeignKeys().SingleOrDefault(key => key.GetConstraintName() == "fk_doctor_province");
        var municipality = doctor.GetForeignKeys().SingleOrDefault(key => key.GetConstraintName() == "fk_doctor_municipality");

        AssertGeographicForeignKey(province, "province", [nameof(Doctor.ProvinceId)], ["id"], "pk_province");
        AssertGeographicForeignKey(municipality, "municipality", [nameof(Doctor.MunicipalityId), nameof(Doctor.ProvinceId)],
            ["id", "province_id"], "uq_municipality_id_province_id");
    }

    [Fact]
    public void DoctorSpecialty_es_owned_con_clave_compuesta_real_y_acceso_por_campo()
    {
        using var context = Context();
        var doctor = context.Model.FindEntityType(typeof(Doctor))!;
        var navigation = doctor.FindNavigation(nameof(Doctor.Specialties))!;
        navigation.FieldInfo!.Name.ShouldBe("_specialties");
        navigation.GetPropertyAccessMode().ShouldBe(PropertyAccessMode.Field);
        var link = navigation.TargetEntityType;
        link.IsOwned().ShouldBeTrue();
        link.GetTableName().ShouldBe("doctor_specialty");
        link.GetSchema().ShouldBe("people");
        link.GetProperties().Select(value => value.Name).ShouldBe(new[] { "DoctorId", "SpecialtyId" }, ignoreOrder: true);
        link.FindPrimaryKey()!.GetName().ShouldBe("pk_doctor_specialty");
        link.FindPrimaryKey()!.Properties.Select(value => value.Name).ShouldBe(new[] { "DoctorId", "SpecialtyId" });
        link.FindProperty("DoctorId")!.IsShadowProperty().ShouldBeTrue();
        link.FindProperty("DoctorId")!.GetColumnName().ShouldBe("doctor_id");
        link.FindProperty("SpecialtyId")!.GetColumnName().ShouldBe("specialty_id");
        link.FindProperty("SpecialtyId")!.ValueGenerated.ShouldBe(ValueGenerated.Never);
        link.FindOwnership()!.GetConstraintName().ShouldBe("fk_doctor_specialty_doctor");
        var specialty = link.GetForeignKeys().Single(key => key.PrincipalEntityType.ClrType == typeof(Specialty));
        specialty.GetConstraintName().ShouldBe("fk_doctor_specialty_specialty");
        specialty.DeleteBehavior.ShouldBe(DeleteBehavior.NoAction);
        doctor.FindProperty(nameof(Doctor.ConsultationFee))!.GetPrecision().ShouldBe(10);
        doctor.FindProperty(nameof(Doctor.ConsultationFee))!.GetScale().ShouldBe(2);
        var fee = doctor.FindProperty(nameof(Doctor.ConsultationFee))!;
        fee.ClrType.ShouldBe(typeof(Money));
        fee.GetTypeMapping().Converter!.ProviderClrType.ShouldBe(typeof(decimal));
        var phone = doctor.FindProperty(nameof(Doctor.PhoneNumber))!;
        phone.ClrType.ShouldBe(typeof(PhoneNumber));
        phone.GetTypeMapping().Converter!.ProviderClrType.ShouldBe(typeof(string));
    }

    private static void AssertGeographicForeignKey(IForeignKey? key, string table, string[] properties,
        string[] principalColumns, string principalKey)
    {
        key.ShouldNotBeNull();
        var principal = key.PrincipalEntityType;
        principal.GetTableName().ShouldBe(table);
        principal.GetSchema().ShouldBe("people");
        key.DeleteBehavior.ShouldBe(DeleteBehavior.NoAction);
        key.IsRequired.ShouldBeTrue();
        key.Properties.Select(property => property.Name).ShouldBe(properties);
        key.Properties.ShouldAllBe(property => !property.IsShadowProperty());
        key.PrincipalKey.Properties.ShouldAllBe(property => !property.IsShadowProperty());
        var principalTable = StoreObjectIdentifier.Table(table, "people");
        key.PrincipalKey.Properties.Select(property => property.GetColumnName(principalTable))
            .ShouldBe(principalColumns);
        key.PrincipalKey.GetName().ShouldBe(principalKey);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql().UseSnakeCaseNamingConvention().Options);
}
