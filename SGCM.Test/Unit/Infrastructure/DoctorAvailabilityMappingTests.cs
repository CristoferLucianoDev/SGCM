using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Infrastructure.Persistence;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class DoctorAvailabilityMappingTests
{
    private static readonly StoreObjectIdentifier SeriesTable =
        StoreObjectIdentifier.Table("doctor_availability_series", "scheduling");

    private static readonly StoreObjectIdentifier BlockTable =
        StoreObjectIdentifier.Table("doctor_availability", "scheduling");

    [Theory]
    [InlineData(typeof(DoctorAvailabilitySeries), "doctor_availability_series", "pk_doctor_availability_series")]
    [InlineData(typeof(DoctorAvailability), "doctor_availability", "pk_doctor_availability")]
    public void Cada_agregado_se_mapea_a_su_tabla_y_clave_con_identidad_always(Type type, string table, string primaryKey)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(type)!;

        entity.GetTableName().ShouldBe(table);
        entity.GetSchema().ShouldBe("scheduling");
        entity.FindPrimaryKey()!.GetName().ShouldBe(primaryKey);
        var id = entity.FindProperty("Id")!;
        id.ValueGenerated.ShouldBe(ValueGenerated.OnAdd);
        id.GetValueGenerationStrategy().ShouldBe(NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);
    }

    [Theory]
    [InlineData(nameof(DoctorAvailabilitySeries.Id), "id", "integer", null, false)]
    [InlineData(nameof(DoctorAvailabilitySeries.DoctorId), "doctor_id", "integer", null, false)]
    [InlineData("_isoDaysOfWeek", "days_of_week", "smallint[]", null, false)]
    [InlineData(nameof(DoctorAvailabilitySeries.StartTime), "start_time", "time without time zone", null, false)]
    [InlineData(nameof(DoctorAvailabilitySeries.EndTime), "end_time", "time without time zone", null, false)]
    [InlineData(nameof(DoctorAvailabilitySeries.StartDate), "start_date", "date", null, false)]
    [InlineData(nameof(DoctorAvailabilitySeries.EndDate), "end_date", "date", null, true)]
    [InlineData(nameof(DoctorAvailabilitySeries.TimeZone), "time_zone", "character varying(50)", 50, false)]
    [InlineData(nameof(DoctorAvailabilitySeries.IsActive), "is_active", "boolean", null, false)]
    public void La_serie_respeta_columnas_tipos_longitudes_y_obligatoriedad(
        string propertyName, string columnName, string columnType, int? maxLength, bool isNullable)
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(DoctorAvailabilitySeries))!.FindProperty(propertyName)!;

        property.GetColumnName(SeriesTable).ShouldBe(columnName);
        property.GetColumnType().ShouldBe(columnType);
        property.GetMaxLength().ShouldBe(maxLength);
        property.IsNullable.ShouldBe(isNullable);
    }

    [Theory]
    [InlineData(nameof(DoctorAvailability.Id), "id", "integer", false)]
    [InlineData(nameof(DoctorAvailability.DoctorId), "doctor_id", "integer", false)]
    [InlineData(nameof(DoctorAvailability.SeriesId), "series_id", "integer", true)]
    [InlineData(nameof(DoctorAvailability.StartsAt), "starts_at", "timestamp with time zone", false)]
    [InlineData(nameof(DoctorAvailability.EndsAt), "ends_at", "timestamp with time zone", false)]
    [InlineData(nameof(DoctorAvailability.IsAvailable), "is_available", "boolean", false)]
    [InlineData(nameof(DoctorAvailability.IsModified), "is_modified", "boolean", false)]
    public void El_bloque_respeta_columnas_tipos_y_obligatoriedad(
        string propertyName, string columnName, string columnType, bool isNullable)
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(DoctorAvailability))!.FindProperty(propertyName)!;

        property.GetColumnName(BlockTable).ShouldBe(columnName);
        property.GetColumnType().ShouldBe(columnType);
        property.IsNullable.ShouldBe(isNullable);
    }

    [Fact]
    public void Los_dias_se_guardan_desde_el_campo_ISO_de_la_serie_y_no_desde_la_propiedad_publica()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(DoctorAvailabilitySeries))!;
        var days = entity.FindProperty("_isoDaysOfWeek")!;

        days.ClrType.ShouldBe(typeof(short[]));
        days.FieldInfo!.Name.ShouldBe("_isoDaysOfWeek");
        days.GetValueConverter().ShouldBeNull();
        entity.FindProperty(nameof(DoctorAvailabilitySeries.DaysOfWeek)).ShouldBeNull();
    }

    [Fact]
    public void La_serie_solo_persiste_sus_datos_y_envia_el_estado_explicitamente()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(DoctorAvailabilitySeries))!;

        entity.GetProperties().Select(property => property.Name).ShouldBe(new[]
        {
            "Id", "DoctorId", "_isoDaysOfWeek", "StartTime", "EndTime", "StartDate", "EndDate", "TimeZone", "IsActive"
        }, ignoreOrder: true);
        entity.FindNavigation(nameof(DoctorAvailabilitySeries.DomainEvents)).ShouldBeNull();
        entity.FindProperty(nameof(DoctorAvailabilitySeries.IsActive))!.ValueGenerated.ShouldBe(ValueGenerated.Never);
        entity.GetForeignKeys().ShouldHaveSingleItem().GetConstraintName().ShouldBe("fk_doctor_availability_series_doctor");
    }

    [Fact]
    public void El_bloque_solo_persiste_sus_datos_y_envia_los_indicadores_explicitamente()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(DoctorAvailability))!;

        entity.GetProperties().Select(property => property.Name).ShouldBe(new[]
        {
            "Id", "DoctorId", "SeriesId", "StartsAt", "EndsAt", "IsAvailable", "IsModified"
        }, ignoreOrder: true);
        entity.FindNavigation(nameof(DoctorAvailability.DomainEvents)).ShouldBeNull();
        entity.FindProperty(nameof(DoctorAvailability.IsAvailable))!.ValueGenerated.ShouldBe(ValueGenerated.Never);
        entity.FindProperty(nameof(DoctorAvailability.IsModified))!.ValueGenerated.ShouldBe(ValueGenerated.Never);
        entity.GetForeignKeys().ShouldHaveSingleItem().GetConstraintName().ShouldBe("fk_doctor_availability_doctor");
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql()
            .UseSnakeCaseNamingConvention()
            .Options);
}
