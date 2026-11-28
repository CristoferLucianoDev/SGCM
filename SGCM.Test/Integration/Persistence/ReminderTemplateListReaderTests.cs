using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

/// <summary>
/// Lista de plantillas contra PostgreSQL: el médico ve sus plantillas activas y las globales activas, estas de solo
/// lectura; no ve las inactivas ni las de otro médico.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReminderTemplateListReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task Devuelve_las_globales_y_despues_las_propias_activas_por_nombre()
    {
        await using var context = CreateContext();
        var (doctorId, otherDoctorId) = await SeedDoctorsAsync(context);
        var zeta = await AddAsync(context, doctorId, "Zeta");
        var alfa = await AddAsync(context, doctorId, "Alfa");
        var inactive = await AddAsync(context, doctorId, "Inactiva");
        inactive.Deactivate().IsSuccess.ShouldBeTrue();
        await context.SaveChangesAsync();
        await AddAsync(context, otherDoctorId, "Ajena");
        var globals = await GlobalsAsync(context);

        var page = await new ReminderTemplateListReader(context).ListAsync(doctorId, 1, 100, CancellationToken.None);

        page.TotalCount.ShouldBe(globals.Count + 2);
        page.Items.ShouldBe(globals
            .Select(global => new ReminderTemplateDto(global.Id, global.Name, global.Subject, global.BodyTemplate, true, IsReadOnly: true))
            .Append(new ReminderTemplateDto(alfa.Id, "Alfa", alfa.Subject, alfa.BodyTemplate, true, IsReadOnly: false))
            .Append(new ReminderTemplateDto(zeta.Id, "Zeta", zeta.Subject, zeta.BodyTemplate, true, IsReadOnly: false))
            .ToArray());
    }

    [Fact]
    public async Task Pagina_con_el_total_de_plantillas_visibles()
    {
        await using var context = CreateContext();
        var (doctorId, _) = await SeedDoctorsAsync(context);
        await AddAsync(context, doctorId, "Alfa");
        var zeta = await AddAsync(context, doctorId, "Zeta");
        var pageSize = (await GlobalsAsync(context)).Count + 1;

        // Las globales y "Alfa" llenan la primera página; "Zeta" queda sola en la segunda.
        var page = await new ReminderTemplateListReader(context).ListAsync(doctorId, 2, pageSize, CancellationToken.None);

        page.PageNumber.ShouldBe(2);
        page.PageSize.ShouldBe(pageSize);
        page.TotalCount.ShouldBe(pageSize + 1);
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(zeta.Id);
    }

    [Fact]
    public void AddInfrastructure_registra_el_lector_con_alcance_por_solicitud()
    {
        using var provider = DoctorPersistenceData.Provider(Database.ConnectionString);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IReminderTemplateListReader>().ShouldBeOfType<ReminderTemplateListReader>();
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);

    private static async Task<(int DoctorId, int OtherDoctorId)> SeedDoctorsAsync(AppDbContext context)
    {
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 0);
        var other = await DoctorPersistenceData.SeedAsync(context, associations: 0);
        return (doctor.DoctorId, other.DoctorId);
    }

    private static async Task<ReminderTemplate> AddAsync(AppDbContext context, int doctorId, string name)
    {
        var template = ReminderTemplate.Create(doctorId, name, "Su cita de {SpecialtyName}", "Hola {PatientName}.").Value;
        context.Set<ReminderTemplate>().Add(template);
        await context.SaveChangesAsync();
        return template;
    }

    // Las globales activas sembradas en la base, en el orden de la lista.
    private static Task<List<ReminderTemplate>> GlobalsAsync(AppDbContext context) =>
        context.Set<ReminderTemplate>().AsNoTracking()
            .Where(template => template.DoctorId == null && template.IsActive)
            .OrderBy(template => template.Name).ThenBy(template => template.Id)
            .ToListAsync();
}
