using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.Scheduling;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

/// <summary>
/// Pruebas de integración del mapeo y del repositorio del registro de reportes contra PostgreSQL, y de su registro en
/// la infraestructura. Las filas se leen con SQL para comprobar lo que realmente quedó en cada columna.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AppointmentReportLogRepositoryTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const int RowCount = 12;

    private static readonly DateTimeOffset GeneratedAt = new(2027, 1, 12, 13, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Guarda_la_generacion_con_sus_filtros_en_jsonb_y_sin_formato()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var criteria = ReportCriteriaMother.Valid() with
        {
            Type = ReportType.Pending,
            Status = AppointmentStatus.Confirmed,
            PatientName = "Ana",
            SortBy = ReportSortField.Patient,
            SortDirection = ReportSortDirection.Descending
        };
        var log = AppointmentReportLog.ForGeneration(doctor.DoctorId, criteria, RowCount, GeneratedAt);

        await SaveAsync(context, log);

        log.Id.ShouldBeGreaterThan(0);
        var row = await ReadRowAsync(log.Id, criteria.ToJson());
        row.ShouldBe(new LogRow(doctor.DoctorId, "generate", "pending", null, RowCount, GeneratedAt, FiltersMatch: true));
    }

    [Fact]
    public async Task Guarda_la_exportacion_con_su_formato()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var log = AppointmentReportLog.ForExport(doctor.DoctorId, ReportCriteriaMother.Valid(), "xlsx", 0, GeneratedAt);

        await SaveAsync(context, log);

        var row = await ReadRowAsync(log.Id, ReportCriteriaMother.Valid().ToJson());
        row.ShouldBe(new LogRow(doctor.DoctorId, "export", "all", "xlsx", 0, GeneratedAt, FiltersMatch: true));
    }

    [Fact]
    public async Task Se_lee_de_vuelta_con_el_mismo_contenido()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var log = AppointmentReportLog.ForExport(doctor.DoctorId, ReportCriteriaMother.Valid(), "pdf", RowCount, GeneratedAt);
        await SaveAsync(context, log);

        await using var readContext = CreateContext();
        var stored = await readContext.Set<AppointmentReportLog>().AsNoTracking().SingleAsync(item => item.Id == log.Id);

        stored.DoctorId.ShouldBe(doctor.DoctorId);
        stored.Action.ShouldBe(ReportAction.Export);
        stored.ReportType.ShouldBe(ReportType.All);
        stored.ExportFormat.ShouldBe("pdf");
        stored.RowCount.ShouldBe(RowCount);
        stored.GeneratedAt.ShouldBe(GeneratedAt);
    }

    [Fact]
    public void AddInfrastructure_registra_el_repositorio_con_alcance_por_solicitud()
    {
        using var provider = DoctorPersistenceData.Provider("Host=localhost;Database=unused;Username=unused;Password=unused");
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var repository = first.ServiceProvider.GetRequiredService<IAppointmentReportLogRepository>();

        repository.ShouldBeOfType<AppointmentReportLogRepository>();
        repository.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IAppointmentReportLogRepository>());
        repository.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IAppointmentReportLogRepository>());
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);

    private static async Task SaveAsync(AppDbContext context, AppointmentReportLog log)
    {
        new AppointmentReportLogRepository(context).Add(log);
        await context.SaveChangesAsync();
    }

    // Los filtros se comparan como jsonb, no como texto, porque PostgreSQL reordena las claves al guardarlos. Las
    // columnas van en snake_case porque SqlQuery aplica la misma convención de nombres que el contexto.
    private async Task<LogRow> ReadRowAsync(int id, string expectedFilters)
    {
        await using var context = CreateContext();

        return (await context.Database.SqlQuery<LogRow>(
            $"""
            SELECT doctor_id, action, report_type, export_format, row_count, generated_at,
                   filters = {expectedFilters}::jsonb AS filters_match
            FROM scheduling.appointment_report_log
            WHERE id = {id}
            """).ToListAsync()).Single();
    }

    private sealed record LogRow(
        int DoctorId,
        string Action,
        string ReportType,
        string? ExportFormat,
        int RowCount,
        DateTimeOffset GeneratedAt,
        bool FiltersMatch);
}
