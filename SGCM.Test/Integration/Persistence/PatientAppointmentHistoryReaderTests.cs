using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
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
/// Pruebas de integración del lector del historial de citas del paciente contra PostgreSQL, y del registro del lector
/// en la infraestructura. Las citas se siembran en horas exactas, como exige la base.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PatientAppointmentHistoryReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const string TimeZone = "America/Santo_Domingo";
    private const int FirstPageSize = 2;
    private const int SecondPage = 2;
    private const int DayCount = 5;
    private const int MissingAppointmentOffset = 1000;
    private const string ReasonForVisit = "Dolor de cabeza persistente";
    private const string StatusReason = "No puedo asistir ese día";

    [Fact]
    public async Task Solo_devuelve_las_citas_del_paciente_pedido_con_medico_especialidad_estado_y_hora_local()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        var otherPatient = await InsertPatientAsync(context);
        // 13:00 UTC son las 09:00 locales del martes 12 de enero en Santo Domingo.
        var startsAt = Utc(2027, 1, 12, 13);
        var appointmentId = await InsertAppointmentAsync(context, patient, doctor, startsAt, "pending");
        await InsertAppointmentAsync(context, otherPatient, doctor, Utc(2027, 1, 12, 15), "pending");

        var result = await ListAsync(patient);

        result.Items.ShouldHaveSingleItem().ShouldBe(new PatientAppointmentDto(
            appointmentId,
            startsAt,
            startsAt.AddHours(AppointmentMother.DurationHours),
            new DateOnly(2027, 1, 12),
            new TimeOnly(9, 0),
            new TimeOnly(11, 0),
            doctor.DoctorId,
            "Luis Pérez",
            doctor.SpecialtyIds[0],
            await SpecialtyNameAsync(context, doctor.SpecialtyIds[0]),
            AppointmentStatus.Pending));
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Ordena_por_inicio_descendente_y_a_igual_inicio_por_id_descendente()
    {
        await using var context = CreateContext();
        var doctorA = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var doctorB = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        var sameStart = Utc(2027, 1, 12, 13);
        // La cancelada no ocupa horario activo, así que el mismo paciente puede tener otra cita a la misma hora.
        var cancelledFirst = await InsertAppointmentAsync(context, patient, doctorA, sameStart, "cancelled");
        var pendingSameStart = await InsertAppointmentAsync(context, patient, doctorB, sameStart, "pending");
        var laterDay = await InsertAppointmentAsync(context, patient, doctorA, Utc(2027, 1, 13, 13), "pending");

        var result = await ListAsync(patient);

        result.Items.Select(item => item.AppointmentId).ShouldBe(new[] { laterDay, pendingSameStart, cancelledFirst });
    }

    [Fact]
    public async Task Filtra_por_estado()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "pending");
        var cancelledId = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 13, 13), "cancelled");

        var result = await ListAsync(patient, status: AppointmentStatus.Cancelled);

        var item = result.Items.ShouldHaveSingleItem();
        item.AppointmentId.ShouldBe(cancelledId);
        item.Status.ShouldBe(AppointmentStatus.Cancelled);
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task El_filtro_por_fechas_locales_incluye_los_extremos_y_excluye_lo_que_cae_fuera()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        // 10:00 locales del 9 de enero: antes del rango.
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 9, 14), "pending");
        // 00:00 locales del 10 de enero: primer día del rango, entra.
        var fromDay = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 10, 4), "pending");
        // 22:00 locales del 20 de enero: último día del rango, entra.
        var toDayLate = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 21, 2), "pending");
        // 00:00 locales del 21 de enero: día siguiente al rango, no entra.
        await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 21, 4), "pending");

        var result = await ListAsync(patient, from: new DateOnly(2027, 1, 10), to: new DateOnly(2027, 1, 20));

        result.Items.Select(item => item.AppointmentId).ShouldBe(new[] { toDayLate, fromDay });
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task La_pagina_trae_el_tramo_pedido_y_el_total_filtrado()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        var ids = new List<int>();
        for (var day = 0; day < DayCount; day++)
        {
            ids.Add(await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 10 + day, 13), "pending"));
        }

        // Descendente: días 14, 13, 12, 11 y 10. La página 2 de tamaño 2 son los días 12 y 11.
        var result = await ListAsync(patient, pageNumber: SecondPage, pageSize: FirstPageSize);

        result.Items.Select(item => item.AppointmentId).ShouldBe(new[] { ids[2], ids[1] });
        result.TotalCount.ShouldBe(DayCount);
    }

    [Fact]
    public async Task Un_paciente_sin_citas_recibe_una_lista_vacia_y_total_cero()
    {
        await using var context = CreateContext();
        var patient = await InsertPatientAsync(context);

        var result = await ListAsync(patient);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task El_detalle_de_una_cita_pendiente_trae_los_mismos_datos_que_la_lista_y_el_motivo_de_consulta()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        var appointmentId = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "pending");
        await SetReasonForVisitAsync(context, appointmentId, ReasonForVisit);

        var detail = await GetAsync(patient, appointmentId);
        var listed = (await ListAsync(patient)).Items.ShouldHaveSingleItem();

        detail.ShouldNotBeNull();
        detail.Appointment.ShouldBe(listed);
        detail.ReasonForVisit.ShouldBe(ReasonForVisit);
        detail.StatusReason.ShouldBeNull();
    }

    [Fact]
    public async Task El_detalle_trae_el_perfil_publico_del_medico_igual_al_de_su_perfil()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        var appointmentId = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "pending");

        var detail = await GetAsync(patient, appointmentId);
        await using var profileContext = CreateContext();
        var profile = await new DoctorProfileReader(profileContext).GetPublicAsync(doctor.DoctorId, CancellationToken.None);

        detail.ShouldNotBeNull();
        profile.ShouldNotBeNull();
        detail.DoctorProfile.ShouldNotBeNull();
        detail.DoctorProfile.Specialties.ShouldBe(profile.Specialties);
        detail.DoctorProfile.ShouldBe(profile with { Specialties = detail.DoctorProfile.Specialties });
    }

    [Fact]
    public async Task Con_un_medico_desactivado_el_detalle_trae_su_nombre_y_la_especialidad_sin_perfil()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        var appointmentId = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "pending");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.doctor SET is_active = false WHERE id = {doctor.DoctorId}");

        var detail = await GetAsync(patient, appointmentId);

        detail.ShouldNotBeNull();
        detail.DoctorProfile.ShouldBeNull();
        detail.Appointment.DoctorId.ShouldBe(doctor.DoctorId);
        detail.Appointment.DoctorName.ShouldNotBeNullOrWhiteSpace();
        detail.Appointment.SpecialtyName.ShouldBe(await SpecialtyNameAsync(context, doctor.SpecialtyIds[0]));
    }

    [Theory]
    [InlineData("cancelled", AppointmentStatus.Cancelled)]
    [InlineData("rejected", AppointmentStatus.Rejected)]
    public async Task El_detalle_de_una_cita_cancelada_o_rechazada_trae_el_motivo_de_estado(
        string status, AppointmentStatus expectedStatus)
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        var appointmentId = await InsertAppointmentAsync(
            context, patient, doctor, Utc(2027, 1, 12, 13), status, StatusReason);

        var detail = await GetAsync(patient, appointmentId);

        detail.ShouldNotBeNull();
        detail.Appointment.Status.ShouldBe(expectedStatus);
        detail.StatusReason.ShouldBe(StatusReason);
    }

    [Fact]
    public async Task El_detalle_de_una_cita_pendiente_sin_motivo_de_consulta_lo_devuelve_nulo()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        var appointmentId = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "pending");

        var detail = await GetAsync(patient, appointmentId);

        detail.ShouldNotBeNull();
        detail.ReasonForVisit.ShouldBeNull();
    }

    [Theory]
    [InlineData("pending", AppointmentStatus.Pending)]
    [InlineData("confirmed", AppointmentStatus.Confirmed)]
    public async Task El_detalle_de_una_cita_activa_no_expone_un_motivo_de_estado_guardado(
        string status, AppointmentStatus expectedStatus)
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        var appointmentId = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), status);
        await SetStatusReasonAsync(context, appointmentId, StatusReason);

        var detail = await GetAsync(patient, appointmentId);

        detail.ShouldNotBeNull();
        detail.Appointment.Status.ShouldBe(expectedStatus);
        detail.StatusReason.ShouldBeNull();
    }

    [Fact]
    public async Task El_detalle_de_una_cita_de_otro_paciente_o_inexistente_es_nulo()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patient = await InsertPatientAsync(context);
        var otherPatient = await InsertPatientAsync(context);
        var appointmentId = await InsertAppointmentAsync(context, patient, doctor, Utc(2027, 1, 12, 13), "pending");

        (await GetAsync(otherPatient, appointmentId)).ShouldBeNull();
        (await GetAsync(patient, appointmentId + MissingAppointmentOffset)).ShouldBeNull();
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

        var reader = first.ServiceProvider.GetRequiredService<IPatientAppointmentHistoryReader>();

        reader.ShouldBeOfType<PatientAppointmentHistoryReader>();
        reader.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IPatientAppointmentHistoryReader>());
        reader.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IPatientAppointmentHistoryReader>());
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);

    private async Task<PagedResult<PatientAppointmentDto>> ListAsync(
        int patientId,
        AppointmentStatus? status = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int pageNumber = 1,
        int pageSize = 20)
    {
        await using var context = CreateContext();
        var options = Options.Create(new SchedulingOptions { TimeZone = TimeZone });

        return await new PatientAppointmentHistoryReader(context, options)
            .ListAsync(patientId, status, from, to, pageNumber, pageSize, CancellationToken.None);
    }

    private async Task<PatientAppointmentDetailData?> GetAsync(int patientId, int appointmentId)
    {
        await using var context = CreateContext();
        var options = Options.Create(new SchedulingOptions { TimeZone = TimeZone });

        return await new PatientAppointmentHistoryReader(context, options)
            .GetAsync(patientId, appointmentId, CancellationToken.None);
    }

    // El sembrador no escribe el motivo de consulta, así que se completa con una actualización directa.
    private static Task<int> SetReasonForVisitAsync(AppDbContext context, int appointmentId, string reason) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE scheduling.appointment SET reason_for_visit = {reason} WHERE id = {appointmentId}");

    // El sembrador no escribe el motivo de estado en citas activas, así que se fuerza con una actualización directa.
    private static Task<int> SetStatusReasonAsync(AppDbContext context, int appointmentId, string reason) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE scheduling.appointment SET status_reason = {reason} WHERE id = {appointmentId}");

    private static async Task<int> InsertPatientAsync(AppDbContext context)
    {
        var user = UserMother.WithRole(UserRole.Patient, email: Email.Create($"historial-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);

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
        string? statusReason = null)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctor.DoctorId}, NULL, {startsAtUtc}, {startsAtUtc.AddHours(AppointmentMother.DurationHours)}, true)
            """);

        return await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patientId, doctor.DoctorId, doctor.SpecialtyIds[0], startsAtUtc, AppointmentMother.DurationHours, status,
            statusReason);
    }

    private static Task<string> SpecialtyNameAsync(AppDbContext context, int specialtyId) =>
        context.Specialties.Where(specialty => specialty.Id == specialtyId).Select(specialty => specialty.Name).SingleAsync();

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);
}
