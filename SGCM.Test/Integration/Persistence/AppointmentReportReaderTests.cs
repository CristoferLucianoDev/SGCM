using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

/// <summary>
/// Pruebas de integración del lector de reportes de citas contra PostgreSQL, y de su registro en la infraestructura.
/// Las citas se siembran en horas exactas y sin cruzarse, como exige la base.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AppointmentReportReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const string TimeZone = "America/Santo_Domingo";
    private const string StatusReason = "No puedo asistir ese día";

    private static readonly AppointmentStatus[] AllStatuses = Enum.GetValues<AppointmentStatus>();
    private static readonly DateTimeOffset RangeStart = Utc(2027, 1, 10, 4);
    private static readonly DateTimeOffset RangeEnd = Utc(2027, 1, 21, 4);

    [Fact]
    public async Task Solo_cuenta_y_lista_las_citas_del_medico_en_los_estados_y_el_rango_pedidos()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var otherDoctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context, "Ana", "Pérez");
        // 00:00 locales del 10 de enero entra; 22:00 locales del 20 de enero, último día, también.
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 10, 4), "pending");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 21, 2), "cancelled");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "rejected", StatusReason);
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 9, 14), "pending");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 21, 4), "pending");
        await InsertAppointmentAsync(context, patient, otherDoctor, Utc(2027, 1, 15, 13), "pending");
        var filter = Filter(doctor.DoctorId, AppointmentStatus.Pending, AppointmentStatus.Cancelled);

        var counts = await CountAsync(filter);
        var rows = await ListAsync(filter);

        counts.ShouldBe(new Dictionary<AppointmentStatus, int>
        {
            [AppointmentStatus.Pending] = 1,
            [AppointmentStatus.Cancelled] = 1
        }, ignoreOrder: true);
        rows.Select(row => row.StartsAt).ShouldBe(new[] { Utc(2027, 1, 10, 4), Utc(2027, 1, 21, 2) });
    }

    [Fact]
    public async Task Devuelve_el_paciente_la_especialidad_el_estado_y_el_motivo_de_consulta()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context, "Ana", "Pérez");
        var startsAt = Utc(2027, 1, 12, 13);
        await InsertAppointmentAsync(context, patient, doctor, startsAt, "confirmed", reasonForVisit: "Control anual");

        var rows = await ListAsync(Filter(doctor.DoctorId, AllStatuses));

        rows.ShouldHaveSingleItem().ShouldBe(new AppointmentReportRowData(
            startsAt, "Ana", "Pérez", await SpecialtyNameAsync(context, doctor.SpecialtyIds[0]),
            AppointmentStatus.Confirmed, "Control anual"));
    }

    [Fact]
    public async Task Sin_estados_el_reporte_queda_vacio()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context, "Ana", "Pérez");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "pending");
        var filter = Filter(doctor.DoctorId);

        (await CountAsync(filter)).ShouldBeEmpty();
        (await ListAsync(filter)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Busca_por_nombre_completo_del_paciente_sin_distinguir_mayusculas_ni_tildes()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var jose = await InsertPatientAsync(context, "José", "Martínez");
        var ana = await InsertPatientAsync(context, "Ana", "Pérez");
        var joseStart = Utc(2027, 1, 12, 13);
        await InsertAppointmentAsync(context, jose, doctor, joseStart, "pending");
        await InsertAppointmentAsync(context, ana, doctor, Utc(2027, 1, 13, 13), "pending");
        var filter = Filter(doctor.DoctorId, AllStatuses) with { PatientName = "JOSE mart" };

        var rows = await ListAsync(filter);

        rows.ShouldHaveSingleItem().StartsAt.ShouldBe(joseStart);
        (await CountAsync(filter))[AppointmentStatus.Pending].ShouldBe(1);
    }

    [Fact]
    public async Task Filtra_por_la_especialidad_de_la_cita()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var otherSpecialty = await TestDataSeeder.InsertSpecialtyAsync(context);
        await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctor.DoctorId, otherSpecialty);
        var patient = await InsertPatientAsync(context, "Ana", "Pérez");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "pending");
        var otherStart = Utc(2027, 1, 13, 13);
        await InsertAppointmentAsync(context, patient, doctor, otherStart, "pending", specialtyId: otherSpecialty);

        var rows = await ListAsync(Filter(doctor.DoctorId, AllStatuses) with { SpecialtyId = otherSpecialty });

        rows.ShouldHaveSingleItem().StartsAt.ShouldBe(otherStart);
    }

    [Fact]
    public async Task Ordena_por_paciente_y_a_igual_paciente_por_inicio_en_el_mismo_sentido()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var ana = await InsertPatientAsync(context, "Ana", "Pérez");
        var bruno = await InsertPatientAsync(context, "Bruno", "Díaz");
        var anaEarly = Utc(2027, 1, 11, 13);
        var anaLate = Utc(2027, 1, 14, 13);
        var brunoStart = Utc(2027, 1, 12, 13);
        await InsertAppointmentAsync(context, ana, doctor, anaLate, "pending");
        await InsertAppointmentAsync(context, bruno, doctor, brunoStart, "pending");
        await InsertAppointmentAsync(context, ana, doctor, anaEarly, "pending");
        var filter = Filter(doctor.DoctorId, AllStatuses);

        var ascending = await ListAsync(filter, ReportSortField.Patient, ReportSortDirection.Ascending);
        var descending = await ListAsync(filter, ReportSortField.Patient, ReportSortDirection.Descending);

        ascending.Select(row => row.StartsAt).ShouldBe(new[] { anaEarly, anaLate, brunoStart });
        descending.Select(row => row.StartsAt).ShouldBe(new[] { brunoStart, anaLate, anaEarly });
    }

    [Fact]
    public async Task Ordena_por_estado_segun_el_ciclo_de_vida_y_no_alfabeticamente()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context, "Ana", "Pérez");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 11, 13), "completed");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "cancelled");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 13, 13), "pending");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 14, 13), "rejected", StatusReason);
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 15, 13), "confirmed");

        var rows = await ListAsync(Filter(doctor.DoctorId, AllStatuses), ReportSortField.Status, ReportSortDirection.Ascending);

        rows.Select(row => row.Status).ShouldBe(AllStatuses);
    }

    [Fact]
    public async Task Pagina_con_desplazamiento_y_limite_y_sin_limite_lee_el_resto()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context, "Ana", "Pérez");
        var starts = new[] { Utc(2027, 1, 11, 13), Utc(2027, 1, 12, 13), Utc(2027, 1, 13, 13) };
        foreach (var start in starts)
            await InsertAppointmentAsync(context, patient, doctor, start, "pending");
        var filter = Filter(doctor.DoctorId, AllStatuses);

        var page = await ListAsync(filter, offset: 1, limit: 1);
        var rest = await ListAsync(filter, offset: 1, limit: null);

        page.ShouldHaveSingleItem().StartsAt.ShouldBe(starts[1]);
        rest.Select(row => row.StartsAt).ShouldBe(starts[1..]);
    }

    [Fact]
    public void AddInfrastructure_registra_el_lector_con_alcance_por_solicitud()
    {
        using var provider = DoctorPersistenceData.Provider("Host=localhost;Database=unused;Username=unused;Password=unused");
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var reader = first.ServiceProvider.GetRequiredService<IAppointmentReportReader>();

        reader.ShouldBeOfType<AppointmentReportReader>();
        reader.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IAppointmentReportReader>());
        reader.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IAppointmentReportReader>());
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);

    private static AppointmentReportFilter Filter(int doctorId, params AppointmentStatus[] statuses) =>
        new(doctorId, statuses, RangeStart, RangeEnd, PatientName: null, SpecialtyId: null);

    private async Task<IReadOnlyDictionary<AppointmentStatus, int>> CountAsync(AppointmentReportFilter filter)
    {
        await using var context = CreateContext();
        return await new AppointmentReportReader(context).CountByStatusAsync(filter, CancellationToken.None);
    }

    private async Task<IReadOnlyList<AppointmentReportRowData>> ListAsync(
        AppointmentReportFilter filter,
        ReportSortField sortBy = ReportSortField.Date,
        ReportSortDirection sortDirection = ReportSortDirection.Ascending,
        int offset = 0,
        int? limit = null)
    {
        await using var context = CreateContext();
        return await new AppointmentReportReader(context)
            .ListAsync(filter, sortBy, sortDirection, offset, limit, CancellationToken.None);
    }

    private static async Task<int> InsertPatientAsync(AppDbContext context, string firstName, string lastName)
    {
        var user = UserMother.WithRole(UserRole.Patient, email: Email.Create($"reporte-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.patient SET first_name = {firstName}, last_name = {lastName} WHERE user_id = {user.Id}");

        return await context.Patients.Where(patient => patient.UserId == user.Id)
            .Select(patient => patient.Id).SingleAsync();
    }

    // El bloque del médico cubre exactamente la cita, para que los bloques de un mismo médico no se crucen.
    private static async Task<int> InsertAppointmentAsync(
        AppDbContext context,
        int patientId,
        DoctorSeed doctor,
        DateTimeOffset startsAtUtc,
        string status,
        string? statusReason = null,
        string? reasonForVisit = null,
        int? specialtyId = null)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctor.DoctorId}, NULL, {startsAtUtc}, {startsAtUtc.AddHours(AppointmentMother.DurationHours)}, true)
            """);

        return await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patientId, doctor.DoctorId, specialtyId ?? doctor.SpecialtyIds[0], startsAtUtc,
            AppointmentMother.DurationHours, status, statusReason, reasonForVisit);
    }

    private static Task<string> SpecialtyNameAsync(AppDbContext context, int specialtyId) =>
        context.Specialties.Where(specialty => specialty.Id == specialtyId).Select(specialty => specialty.Name).SingleAsync();

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);
}
