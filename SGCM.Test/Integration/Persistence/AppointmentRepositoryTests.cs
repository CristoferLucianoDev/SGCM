using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class AppointmentRepositoryTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const int DurationHours = AppointmentMother.DurationHours;

    // Martes 12 de enero de 2027 a las 9:00 en Santo Domingo; el bloque del médico cubre todo enero.
    private static readonly DateTimeOffset Start = AppointmentMother.StartsAt;
    private static readonly DateTimeOffset BlockStart = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BlockEnd = new(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeRange Window = TimeRange.Create(BlockStart, BlockEnd).Value;

    [Fact]
    public async Task La_cita_se_guarda_pendiente_y_se_lee_completa()
    {
        await using var provider = CreateProvider();
        var doctor = await SeedDoctorAsync(provider);
        var patientId = await InsertPatientAsync(provider);

        var id = await SaveAsync(provider, Request(patientId, doctor, Start, "Control anual"));

        await using var scope = provider.CreateAsyncScope();
        var saved = await scope.ServiceProvider.GetRequiredService<IAppointmentRepository>()
            .GetByIdAsync(id, CancellationToken.None);
        saved.ShouldNotBeNull();
        saved.PatientId.ShouldBe(patientId);
        saved.DoctorId.ShouldBe(doctor.DoctorId);
        saved.SpecialtyId.ShouldBe(doctor.SpecialtyIds[0]);
        saved.StartsAt.ShouldBe(Start);
        saved.EndsAt.ShouldBe(Start.AddHours(DurationHours));
        saved.Status.ShouldBe(AppointmentStatus.Pending);
        saved.ReasonForVisit.ShouldBe("Control anual");
        saved.StatusReason.ShouldBeNull();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.SqlQuery<string>(
                $"SELECT status AS \"Value\" FROM scheduling.appointment WHERE id = {id}")
            .SingleAsync()).ShouldBe("pending");
    }

    [Fact]
    public async Task GetByIdAsync_de_una_cita_que_no_existe_devuelve_null()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<IAppointmentRepository>()
            .GetByIdAsync(int.MaxValue, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GetByIdForUpdateAsync_sin_transaccion_lanza_InvalidOperationException()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IAppointmentRepository>()
                .GetByIdForUpdateAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdForUpdateAsync_en_una_transaccion_devuelve_la_cita_guardada_y_rastreada()
    {
        await using var provider = CreateProvider();
        var doctor = await SeedDoctorAsync(provider);
        var id = await SaveAsync(provider, Request(await InsertPatientAsync(provider), doctor, Start));
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var locked = await scope.ServiceProvider.GetRequiredService<IAppointmentRepository>()
            .GetByIdForUpdateAsync(id, CancellationToken.None);

        locked.ShouldNotBeNull();
        locked.Id.ShouldBe(id);
        locked.Status.ShouldBe(AppointmentStatus.Pending);
        context.ChangeTracker.Entries<Appointment>().Select(entry => entry.Entity).ShouldContain(locked);
    }

    [Fact]
    public async Task GetByIdForUpdateAsync_relee_una_cita_ya_rastreada_tras_el_bloqueo()
    {
        await using var provider = CreateProvider();
        var doctor = await SeedDoctorAsync(provider);
        var id = await SaveAsync(provider, Request(await InsertPatientAsync(provider), doctor, Start));
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAppointmentRepository>();
        (await repository.GetByIdAsync(id, CancellationToken.None))!.Status.ShouldBe(AppointmentStatus.Pending);
        await using (var other = provider.CreateAsyncScope())
        {
            await other.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE scheduling.appointment SET status = 'cancelled' WHERE id = {id}");
        }

        await using var transaction = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Database.BeginTransactionAsync();
        var locked = await repository.GetByIdForUpdateAsync(id, CancellationToken.None);

        locked.ShouldNotBeNull();
        locked.Status.ShouldBe(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task GetByIdForUpdateAsync_con_cambios_pendientes_en_la_cita_rastreada_lanza_InvalidOperationException()
    {
        await using var provider = CreateProvider();
        var doctor = await SeedDoctorAsync(provider);
        var id = await SaveAsync(provider, Request(await InsertPatientAsync(provider), doctor, Start));
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAppointmentRepository>();
        var tracked = await repository.GetByIdAsync(id, CancellationToken.None);
        tracked!.Cancel(
            AppointmentCanceller.Patient, null, Start.AddHours(-AppointmentMother.PatientCancellationDeadlineHours * 2),
            AppointmentMother.PatientCancellationDeadlineHours, AppointmentMother.ShortNotice).IsSuccess.ShouldBeTrue();
        await using var transaction = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Database.BeginTransactionAsync();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            repository.GetByIdForUpdateAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdForUpdateAsync_de_una_cita_que_no_existe_devuelve_null()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var transaction = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Database.BeginTransactionAsync();

        (await scope.ServiceProvider.GetRequiredService<IAppointmentRepository>()
            .GetByIdForUpdateAsync(int.MaxValue, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Una_cita_que_se_cruza_con_otra_activa_del_medico_es_horario_no_disponible()
    {
        await using var provider = CreateProvider();
        var doctor = await SeedDoctorAsync(provider);
        await SaveAsync(provider, Request(await InsertPatientAsync(provider), doctor, Start));
        var otherPatient = await InsertPatientAsync(provider);

        var conflict = await Should.ThrowAsync<PersistenceConflictException>(
            () => SaveAsync(provider, Request(otherPatient, doctor, Start.AddHours(1))));

        conflict.Error.ShouldBe(AppointmentErrors.SlotUnavailable);
        (await CountAsync(provider, doctor.DoctorId)).ShouldBe(1);
    }

    [Fact]
    public async Task Una_cita_que_se_cruza_con_otra_activa_del_paciente_es_traslape_con_otra_cita_tuya()
    {
        await using var provider = CreateProvider();
        var doctor = await SeedDoctorAsync(provider);
        var otherDoctor = await SeedDoctorAsync(provider);
        var patientId = await InsertPatientAsync(provider);
        await SaveAsync(provider, Request(patientId, doctor, Start));

        var conflict = await Should.ThrowAsync<PersistenceConflictException>(
            () => SaveAsync(provider, Request(patientId, otherDoctor, Start.AddHours(1))));

        conflict.Error.ShouldBe(AppointmentErrors.PatientOverlap);
        (await CountAsync(provider, otherDoctor.DoctorId)).ShouldBe(0);
    }

    [Fact]
    public async Task Citas_contiguas_del_mismo_medico_y_del_mismo_paciente_se_aceptan()
    {
        await using var provider = CreateProvider();
        var doctor = await SeedDoctorAsync(provider);
        var patientId = await InsertPatientAsync(provider);

        await SaveAsync(provider, Request(patientId, doctor, Start));
        await SaveAsync(provider, Request(patientId, doctor, Start.AddHours(DurationHours)));

        (await CountAsync(provider, doctor.DoctorId)).ShouldBe(2);
    }

    [Fact]
    public async Task Una_cita_cancelada_ya_no_ocupa_el_horario()
    {
        await using var provider = CreateProvider();
        var doctor = await SeedDoctorAsync(provider);
        var cancelled = await SaveAsync(provider, Request(await InsertPatientAsync(provider), doctor, Start));
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE scheduling.appointment SET status = 'cancelled' WHERE id = {cancelled}");
        }

        await SaveAsync(provider, Request(await InsertPatientAsync(provider), doctor, Start));

        (await CountAsync(provider, doctor.DoctorId)).ShouldBe(2);
    }

    private static Appointment Request(int patientId, DoctorSeed doctor, DateTimeOffset startsAt, string? reason = null) =>
        Appointment.Request(patientId, doctor.DoctorId, doctor.SpecialtyIds[0], startsAt, reason, DurationHours, Window).Value;

    private static async Task<int> SaveAsync(ServiceProvider provider, Appointment appointment)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IAppointmentRepository>().Add(appointment);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return appointment.Id;
    }

    private static async Task<int> CountAsync(ServiceProvider provider, int doctorId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Appointments
            .CountAsync(appointment => appointment.DoctorId == doctorId);
    }

    /// <summary>Crea el médico con una especialidad activa y un bloque abierto que cubre todo enero de 2027.</summary>
    private static async Task<DoctorSeed> SeedDoctorAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({seed.DoctorId}, NULL, {BlockStart}, {BlockEnd}, true)
            """);
        return seed;
    }

    private static async Task<int> InsertPatientAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = UserMother.WithRole(
            UserRole.Patient, email: Email.Create($"paciente-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
        return await context.Patients.Where(patient => patient.UserId == user.Id)
            .Select(patient => patient.Id).SingleAsync();
    }

    private ServiceProvider CreateProvider() => DoctorPersistenceData.Provider(Database.ConnectionString);
}
