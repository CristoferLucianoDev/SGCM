using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

/// <summary>
/// Pruebas del lector de la agenda del médico contra la vista <c>scheduling.vw_doctor_schedule</c>. Las citas se
/// siembran en horas exactas y cada una con su propio bloque, como exige la base.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DoctorScheduleReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const string TimeZone = "America/Santo_Domingo";
    private static readonly DoctorScheduleFilter NoFilter = new(null, null, null, null, DoctorScheduleSort.Ascending);

    [Fact]
    public async Task Solo_devuelve_las_citas_activas_del_medico_con_paciente_especialidad_estado_y_hora_local()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var other = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context, "José", "Martínez");
        // 13:00 UTC son las 09:00 locales del martes 12 de enero en Santo Domingo.
        var startsAt = Utc(2027, 1, 12, 13);
        var id = await InsertAppointmentAsync(context, patient, doctor, startsAt, "confirmed");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 13, 13), "cancelled");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 14, 13), "completed");
        await InsertAppointmentAsync(context, patient, other, Utc(2027, 1, 12, 15), "pending");

        var result = await ListAsync(doctor, NoFilter);

        result.TotalCount.ShouldBe(1);
        result.Items.ShouldHaveSingleItem().ShouldBe(new DoctorScheduleItemDto(
            id,
            patient,
            "José Martínez",
            doctor.SpecialtyIds[0],
            await SpecialtyNameAsync(context, doctor.SpecialtyIds[0]),
            startsAt,
            startsAt.AddHours(AppointmentMother.DurationHours),
            new DateOnly(2027, 1, 12),
            new TimeOnly(9, 0),
            new TimeOnly(9 + AppointmentMother.DurationHours, 0),
            AppointmentStatus.Confirmed,
            IsInProgress: false));
    }

    [Fact]
    public async Task Ordena_por_fecha_en_los_dos_sentidos()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context, "Ana", "Pérez");
        var second = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 13, 13), "pending");
        var first = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "pending");
        var third = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 14, 13), "pending");

        (await ListAsync(doctor, NoFilter)).Items.Select(item => item.AppointmentId).ShouldBe(new[] { first, second, third });
        (await ListAsync(doctor, NoFilter with { Sort = DoctorScheduleSort.Descending })).Items
            .Select(item => item.AppointmentId).ShouldBe(new[] { third, second, first });
    }

    [Fact]
    public async Task Filtra_por_estado()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context, "Ana", "Pérez");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "pending");
        var confirmed = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 13, 13), "confirmed");

        var result = await ListAsync(doctor, NoFilter with { Status = AppointmentStatus.Confirmed });

        result.Items.ShouldHaveSingleItem().AppointmentId.ShouldBe(confirmed);
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task El_rango_de_fechas_locales_incluye_los_extremos_y_excluye_lo_que_cae_fuera()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context, "Ana", "Pérez");
        // En Santo Domingo (UTC-4): 01:00 UTC del 12 es el 11 a las 21:00 y 04:00 UTC del 12 es el 12 a las 00:00;
        // 02:00 UTC del 15 es el 14 a las 22:00 y 04:00 UTC del 15 es el 15 a las 00:00. Las citas no se cruzan.
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 1), "pending");
        var first = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 4), "pending");
        var last = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 15, 2), "pending");
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 15, 4), "pending");

        var result = await ListAsync(doctor, NoFilter with { From = new DateOnly(2027, 1, 12), To = new DateOnly(2027, 1, 14) });

        result.Items.Select(item => item.AppointmentId).ShouldBe(new[] { first, last });
    }

    [Theory]
    [InlineData("perez")]
    [InlineData("PÉREZ")]
    [InlineData("maria pe")]
    [InlineData("  Maria  ")]
    public async Task Busca_por_parte_del_nombre_sin_mayusculas_ni_tildes(string search)
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var maria = await InsertPatientAsync(context, "María", "Pérez");
        var luis = await InsertPatientAsync(context, "Luis", "Gómez");
        var expected = await InsertAppointmentAsync(context, maria, doctor, Utc(2027, 1, 12, 13), "pending");
        await InsertAppointmentAsync(context, luis, doctor, Utc(2027, 1, 13, 13), "pending");

        var result = await ListAsync(doctor, NoFilter with { PatientName = search.Trim() });

        result.Items.ShouldHaveSingleItem().AppointmentId.ShouldBe(expected);
    }

    [Fact]
    public async Task Combina_estado_fechas_y_paciente()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var maria = await InsertPatientAsync(context, "María", "Pérez");
        var luis = await InsertPatientAsync(context, "Luis", "Gómez");
        var expected = await InsertAppointmentAsync(context, maria, doctor, Utc(2027, 1, 12, 13), "confirmed");
        await InsertAppointmentAsync(context, maria, doctor, Utc(2027, 1, 12, 16), "pending");
        await InsertAppointmentAsync(context, maria, doctor, Utc(2027, 1, 20, 13), "confirmed");
        await InsertAppointmentAsync(context, luis, doctor, Utc(2027, 1, 13, 13), "confirmed");

        var result = await ListAsync(doctor, new DoctorScheduleFilter(
            AppointmentStatus.Confirmed, new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 17), "maria", DoctorScheduleSort.Ascending));

        result.Items.ShouldHaveSingleItem().AppointmentId.ShouldBe(expected);
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task La_pagina_trae_el_tramo_pedido_y_el_total_filtrado()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context, "Ana", "Pérez");
        var ids = new List<int>();
        for (var day = 12; day <= 16; day++)
            ids.Add(await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, day, 13), "pending"));

        var page = await ListAsync(doctor, NoFilter, pageNumber: 2, pageSize: 2);

        page.Items.Select(item => item.AppointmentId).ShouldBe(ids.Skip(2).Take(2));
        page.PageNumber.ShouldBe(2);
        page.PageSize.ShouldBe(2);
        page.TotalCount.ShouldBe(5);
    }

    [Fact]
    public async Task El_rango_sin_paginar_devuelve_todas_las_citas_del_rango_en_orden()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context, "Ana", "Pérez");
        var ids = new List<int>();
        for (var day = 1; day <= 25; day++)
            ids.Add(await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, day, 13), "pending"));
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 2, 1, 13), "pending");

        var items = await new DoctorScheduleReader(context, Options.Create(new SchedulingOptions { TimeZone = TimeZone }))
            .ListRangeAsync(doctor.DoctorId, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), CancellationToken.None);

        items.Select(item => item.AppointmentId).ShouldBe(ids);
    }

    [Fact]
    public void AddInfrastructure_registra_el_lector_con_alcance_por_solicitud()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options =>
            options.SgcmDatabase = "Host=localhost;Database=unused;Username=unused;Password=unused");
        services.Configure<SchedulingOptions>(options => options.TimeZone = TimeZone);
        services.AddInfrastructure();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var reader = first.ServiceProvider.GetRequiredService<IDoctorScheduleReader>();

        reader.ShouldBeOfType<DoctorScheduleReader>();
        reader.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IDoctorScheduleReader>());
        reader.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IDoctorScheduleReader>());
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);

    private async Task<PagedResult<DoctorScheduleItemDto>> ListAsync(
        DoctorSeed doctor, DoctorScheduleFilter filter, int pageNumber = 1, int pageSize = 20)
    {
        await using var context = CreateContext();
        return await new DoctorScheduleReader(context, Options.Create(new SchedulingOptions { TimeZone = TimeZone }))
            .ListAsync(doctor.DoctorId, filter, pageNumber, pageSize, CancellationToken.None);
    }

    private static async Task<int> InsertPatientAsync(AppDbContext context, string firstName, string lastName)
    {
        var user = UserMother.WithRole(UserRole.Patient, email: Email.Create($"agenda-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO people.patient (user_id, first_name, last_name, birth_date, phone_number)
            VALUES ({user.Id}, {firstName}, {lastName}, {new DateOnly(1990, 5, 20)}, '8095550100')
            RETURNING id AS "Value"
            """).ToListAsync()).Single();
    }

    // El bloque del médico cubre exactamente la cita, para que los bloques de un mismo médico no se crucen.
    private static async Task<int> InsertAppointmentAsync(
        AppDbContext context, int patientId, DoctorSeed doctor, DateTimeOffset startsAtUtc, string status)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctor.DoctorId}, NULL, {startsAtUtc}, {startsAtUtc.AddHours(AppointmentMother.DurationHours)}, true)
            """);

        return await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patientId, doctor.DoctorId, doctor.SpecialtyIds[0], startsAtUtc, AppointmentMother.DurationHours, status);
    }

    private static Task<string> SpecialtyNameAsync(AppDbContext context, int specialtyId) =>
        context.Specialties.Where(specialty => specialty.Id == specialtyId).Select(specialty => specialty.Name).SingleAsync();

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);
}
