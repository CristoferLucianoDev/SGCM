using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class AppointmentNotificationReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private static readonly DateTimeOffset BlockStart = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BlockEnd = new(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_trae_paciente_medico_especialidad_tarifa_y_la_hora_en_Santo_Domingo()
    {
        var seed = await SeedAsync();
        // 13:00 a 15:00 UTC son las 9:00 a 11:00 del martes 12 de enero en Santo Domingo.
        var id = await InsertAppointmentAsync(seed, new DateTimeOffset(2027, 1, 12, 13, 0, 0, TimeSpan.Zero), "Control anual");

        var data = await GetAsync(id);

        data.ShouldBe(new AppointmentNotificationData(
            id,
            "Ana Pérez",
            seed.PatientEmail,
            "Luis Pérez",
            seed.DoctorEmail,
            seed.SpecialtyName,
            1500m,
            new DateOnly(2027, 1, 12),
            new TimeOnly(9, 0),
            new TimeOnly(11, 0),
            "Control anual",
            await ExpectedAddressAsync(seed.DoctorId)));
    }

    [Fact]
    public async Task GetAsync_usa_la_fecha_local_cuando_en_UTC_ya_es_el_dia_siguiente()
    {
        var seed = await SeedAsync();
        // 02:00 UTC del 13 de enero son las 22:00 del 12 en Santo Domingo.
        var id = await InsertAppointmentAsync(seed, new DateTimeOffset(2027, 1, 13, 2, 0, 0, TimeSpan.Zero), null);

        var data = (await GetAsync(id)).ShouldNotBeNull();

        data.AppointmentDate.ShouldBe(new DateOnly(2027, 1, 12));
        data.StartTime.ShouldBe(new TimeOnly(22, 0));
        data.EndTime.ShouldBe(new TimeOnly(0, 0));
        data.ReasonForVisit.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_lee_la_cita_en_cualquier_estado()
    {
        var seed = await SeedAsync();
        var id = await InsertAppointmentAsync(seed, new DateTimeOffset(2027, 1, 12, 13, 0, 0, TimeSpan.Zero), null);
        await using (var context = CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE scheduling.appointment SET status = 'cancelled' WHERE id = {id}");
        }

        (await GetAsync(id)).ShouldNotBeNull().AppointmentId.ShouldBe(id);
    }

    [Fact]
    public async Task GetAsync_de_una_cita_que_no_existe_devuelve_null()
    {
        (await GetAsync(int.MaxValue)).ShouldBeNull();
    }

    private sealed record Seed(int PatientId, string PatientEmail, int DoctorId, string DoctorEmail, int SpecialtyId, string SpecialtyName);

    private async Task<Seed> SeedAsync()
    {
        await using var context = CreateContext();
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctor.DoctorId}, NULL, {BlockStart}, {BlockEnd}, true)
            """);
        var doctorEmail = (await context.Users.Where(user => user.Id == doctor.UserId)
            .Select(user => user.Email).SingleAsync()).Value;
        var specialtyName = await context.Specialties.Where(specialty => specialty.Id == doctor.SpecialtyIds[0])
            .Select(specialty => specialty.Name).SingleAsync();

        var patientEmail = $"paciente-{Guid.NewGuid():N}@correo.com";
        var patientUser = UserMother.WithRole(UserRole.Patient, email: Email.Create(patientEmail).Value);
        context.Users.Add(patientUser);
        await context.SaveChangesAsync();
        await TestDataSeeder.InsertPatientProfileAsync(context, patientUser.Id);
        var patientId = await context.Patients.Where(patient => patient.UserId == patientUser.Id)
            .Select(patient => patient.Id).SingleAsync();

        return new Seed(patientId, patientEmail, doctor.DoctorId, doctorEmail, doctor.SpecialtyIds[0], specialtyName);
    }

    private async Task<int> InsertAppointmentAsync(Seed seed, DateTimeOffset startsAt, string? reason)
    {
        await using var context = CreateContext();
        var endsAt = startsAt.AddHours(AppointmentMother.DurationHours);
        return (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO scheduling.appointment (patient_id, doctor_id, specialty_id, starts_at, ends_at, status, reason_for_visit)
            VALUES ({seed.PatientId}, {seed.DoctorId}, {seed.SpecialtyId}, {startsAt}, {endsAt}, 'pending', {reason})
            RETURNING id AS "Value"
            """).ToListAsync()).Single();
    }

    private async Task<AppointmentNotificationData?> GetAsync(int appointmentId)
    {
        await using var context = CreateContext();
        var options = Options.Create(new SchedulingOptions { TimeZone = "America/Santo_Domingo" });
        return await new AppointmentNotificationReader(context, options).GetAsync(appointmentId, CancellationToken.None);
    }

    // La dirección es la calle del perfil del médico con su municipio y su provincia.
    private async Task<string> ExpectedAddressAsync(int doctorId)
    {
        await using var context = CreateContext();
        return (await context.Database.SqlQuery<string>(
            $"""
            SELECT doctor.address_line || ', ' || municipality.name || ', ' || province.name AS "Value"
            FROM people.doctor doctor
            JOIN people.municipality municipality ON municipality.id = doctor.municipality_id
            JOIN people.province province ON province.id = doctor.province_id
            WHERE doctor.id = {doctorId}
            """).ToListAsync()).Single();
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);
}
