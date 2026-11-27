using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.Clinical;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

/// <summary>
/// Pruebas de integración del mapeo y del repositorio del registro de accesos al registro clínico contra PostgreSQL,
/// y de su registro en la infraestructura. Las filas se leen con SQL para comprobar lo que realmente quedó.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MedicalRecordAccessLogRepositoryTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private static readonly DateTimeOffset AccessedAt = new(2026, 3, 10, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(MedicalRecordAccessType.View, "view")]
    [InlineData(MedicalRecordAccessType.Create, "create")]
    [InlineData(MedicalRecordAccessType.Update, "update")]
    public async Task Guarda_el_acceso_con_su_tipo_en_minusculas(MedicalRecordAccessType accessType, string stored)
    {
        await using var context = CreateContext();
        var (seed, record) = await ClinicalPersistenceData.SeedMedicalRecordAsync(context);
        var log = MedicalRecordAccessLog.Record(record.Id, seed.DoctorUserId, accessType, AccessedAt, "2001:db8::1");

        await SaveAsync(context, log);

        log.Id.ShouldBeGreaterThan(0);
        (await ReadRowAsync(log.Id)).ShouldBe(
            new AccessRow(record.Id, seed.DoctorUserId, stored, AccessedAt, "2001:db8::1"));
    }

    [Fact]
    public async Task Sin_ip_se_guarda_null()
    {
        await using var context = CreateContext();
        var (seed, record) = await ClinicalPersistenceData.SeedMedicalRecordAsync(context);
        var log = MedicalRecordAccessLog.Record(
            record.Id, seed.DoctorUserId, MedicalRecordAccessType.Create, AccessedAt, ipAddress: null);

        await SaveAsync(context, log);

        (await ReadRowAsync(log.Id)).IpAddress.ShouldBeNull();
    }

    [Fact]
    public async Task Se_lee_de_vuelta_con_el_mismo_contenido()
    {
        await using var context = CreateContext();
        var (seed, record) = await ClinicalPersistenceData.SeedMedicalRecordAsync(context);
        var log = MedicalRecordAccessLog.Record(
            record.Id, seed.DoctorUserId, MedicalRecordAccessType.Update, AccessedAt, "10.0.0.5");
        await SaveAsync(context, log);

        await using var readContext = CreateContext();
        var stored = await readContext.Set<MedicalRecordAccessLog>().AsNoTracking().SingleAsync(item => item.Id == log.Id);

        stored.MedicalRecordId.ShouldBe(record.Id);
        stored.AccessedBy.ShouldBe(seed.DoctorUserId);
        stored.AccessType.ShouldBe(MedicalRecordAccessType.Update);
        stored.AccessedAt.ShouldBe(AccessedAt);
        stored.IpAddress.ShouldBe("10.0.0.5");
    }

    [Fact]
    public void AddInfrastructure_registra_el_repositorio_con_alcance_por_solicitud()
    {
        using var provider = DoctorPersistenceData.Provider("Host=localhost;Database=unused;Username=unused;Password=unused");
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var repository = first.ServiceProvider.GetRequiredService<IMedicalRecordAccessLogRepository>();

        repository.ShouldBeOfType<MedicalRecordAccessLogRepository>();
        repository.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IMedicalRecordAccessLogRepository>());
        repository.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IMedicalRecordAccessLogRepository>());
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);

    private static async Task SaveAsync(AppDbContext context, MedicalRecordAccessLog log)
    {
        new MedicalRecordAccessLogRepository(context).Add(log);
        await context.SaveChangesAsync();
    }

    // Las columnas van en snake_case porque SqlQuery aplica la misma convención de nombres que el contexto.
    private async Task<AccessRow> ReadRowAsync(int id)
    {
        await using var context = CreateContext();
        return (await context.Database.SqlQuery<AccessRow>(
            $"""
            SELECT medical_record_id, accessed_by, access_type, accessed_at, ip_address
            FROM clinical.medical_record_access_log
            WHERE id = {id}
            """).ToListAsync()).Single();
    }

    private sealed record AccessRow(
        int MedicalRecordId, int AccessedBy, string AccessType, DateTimeOffset AccessedAt, string? IpAddress);
}
