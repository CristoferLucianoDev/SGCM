using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class ActiveAppointmentReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const string Pending = "pending";
    private const string Confirmed = "confirmed";
    private const string Rejected = "rejected";
    private const string Cancelled = "cancelled";
    private const string Completed = "completed";

    // Todas las citas caen en enero de 2027, dentro del único bloque abierto que se siembra por médico.
    private static readonly DateTimeOffset BlockStart = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BlockEnd = new(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day = new(2027, 1, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListActiveIntervalsAsync_solo_trae_las_citas_pendientes_y_confirmadas()
    {
        var doctor = await SeedDoctorAsync();
        var pending = await InsertAsync(doctor, At(8), At(10), Pending);
        var confirmed = await InsertAsync(doctor, At(10), At(12), Confirmed);
        await InsertAsync(doctor, At(12), At(14), Rejected);
        await InsertAsync(doctor, At(14), At(16), Cancelled);
        await InsertAsync(doctor, At(16), At(18), Completed);

        var intervals = await ListAsync(doctor.DoctorId, Day, Day.AddDays(1));

        intervals.Select(interval => interval.AppointmentId).ShouldBe(new[] { pending, confirmed });
    }

    [Fact]
    public async Task ListActiveIntervalsAsync_filtra_por_medico()
    {
        var doctor = await SeedDoctorAsync();
        var otherDoctor = await SeedDoctorAsync();
        var own = await InsertAsync(doctor, At(8), At(10), Confirmed);
        await InsertAsync(otherDoctor, At(8), At(10), Confirmed);

        var intervals = await ListAsync(doctor.DoctorId, Day, Day.AddDays(1));

        intervals.Select(interval => interval.AppointmentId).ShouldBe(new[] { own });
    }

    [Fact]
    public async Task ListActiveIntervalsAsync_solo_trae_las_que_se_cruzan_con_el_rango_y_excluye_los_extremos_contiguos()
    {
        var doctor = await SeedDoctorAsync();
        await InsertAsync(doctor, At(7), At(9), Confirmed);
        var endsAtFrom = await InsertAsync(doctor, At(9), At(11), Confirmed);
        var startsAtFrom = await InsertAsync(doctor, At(11), At(13), Confirmed);
        var inside = await InsertAsync(doctor, At(13), At(15), Confirmed);
        var crossesTo = await InsertAsync(doctor, At(15), At(17), Confirmed);
        await InsertAsync(doctor, At(17), At(19), Confirmed);

        // [11:00, 17:00): la cita que termina justo a las 11:00 y la que empieza justo a las 17:00 no entran.
        var exact = await ListAsync(doctor.DoctorId, At(11), At(17));
        // [10:00, 17:00): ahora la cita 9:00 a 11:00 cruza el inicio del rango y sí entra.
        var crossesFrom = await ListAsync(doctor.DoctorId, At(10), At(17));

        exact.Select(interval => interval.AppointmentId).ShouldBe(new[] { startsAtFrom, inside, crossesTo });
        crossesFrom.Select(interval => interval.AppointmentId).ShouldBe(new[] { endsAtFrom, startsAtFrom, inside, crossesTo });
    }

    [Fact]
    public async Task ListActiveIntervalsAsync_las_ordena_por_inicio_sin_importar_el_orden_de_insercion()
    {
        var doctor = await SeedDoctorAsync();
        var late = await InsertAsync(doctor, At(14), At(16), Pending);
        var early = await InsertAsync(doctor, At(8), At(10), Pending);
        var middle = await InsertAsync(doctor, At(10), At(12), Confirmed);

        var intervals = await ListAsync(doctor.DoctorId, Day, Day.AddDays(1));

        intervals.Select(interval => interval.AppointmentId).ShouldBe(new[] { early, middle, late });
    }

    [Fact]
    public async Task ListActiveIntervalsAsync_devuelve_instantes_en_UTC_y_conserva_el_fin_real_de_una_cita_que_no_termina_en_punto()
    {
        var doctor = await SeedDoctorAsync();
        var id = await InsertAsync(doctor, At(10), At(11).AddMinutes(30), Confirmed);

        var interval = (await ListAsync(doctor.DoctorId, Day, Day.AddDays(1))).ShouldHaveSingleItem();

        interval.AppointmentId.ShouldBe(id);
        interval.Range.Start.ShouldBe(At(10));
        interval.Range.End.ShouldBe(At(11).AddMinutes(30));
        interval.Range.Start.Offset.ShouldBe(TimeSpan.Zero);
        interval.Range.End.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task ListActiveIntervalsAsync_acepta_un_rango_con_otro_desfase_y_lo_compara_por_instante()
    {
        var doctor = await SeedDoctorAsync();
        var id = await InsertAsync(doctor, At(10), At(12), Confirmed);
        var utcMinusFour = TimeSpan.FromHours(-4);

        // [08:00, 09:00) en UTC-4 es [12:00, 13:00) UTC: la cita de 10:00 a 12:00 UTC termina justo al inicio.
        var outside = await ListAsync(doctor.DoctorId, At(12).ToOffset(utcMinusFour), At(13).ToOffset(utcMinusFour));
        // [07:00, 08:00) en UTC-4 es [11:00, 12:00) UTC: sí cruza la cita.
        var inside = await ListAsync(doctor.DoctorId, At(11).ToOffset(utcMinusFour), At(12).ToOffset(utcMinusFour));

        outside.ShouldBeEmpty();
        inside.Select(interval => interval.AppointmentId).ShouldBe(new[] { id });
    }

    [Fact]
    public async Task ListActiveIntervalsAsync_sin_citas_devuelve_una_lista_vacia()
    {
        var doctor = await SeedDoctorAsync();

        (await ListAsync(doctor.DoctorId, Day, Day.AddDays(1))).ShouldBeEmpty();
    }

    private static DateTimeOffset At(int hour) => Day.AddHours(hour);

    /// <summary>Crea el médico con una especialidad activa y un bloque abierto que cubre todo enero de 2027.</summary>
    private async Task<DoctorSeed> SeedDoctorAsync()
    {
        await using var context = CreateContext();
        var seed = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({seed.DoctorId}, NULL, {BlockStart}, {BlockEnd}, true)
            """);
        return seed;
    }

    /// <summary>
    /// Inserta la cita de un paciente nuevo, para no chocar con la restricción de traslape del paciente.
    /// La base solo permite crearla pendiente o confirmada, así que los estados finales se alcanzan por cambio de estado.
    /// </summary>
    private async Task<int> InsertAsync(DoctorSeed doctor, DateTimeOffset startsAt, DateTimeOffset endsAt, string status)
    {
        await using var context = CreateContext();
        var patientId = await InsertPatientAsync(context);
        var creationStatus = status is Completed ? Confirmed : status is Rejected or Cancelled ? Pending : status;
        var id = (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO scheduling.appointment (patient_id, doctor_id, specialty_id, starts_at, ends_at, status)
            VALUES ({patientId}, {doctor.DoctorId}, {doctor.SpecialtyIds[0]}, {startsAt}, {endsAt}, {creationStatus})
            RETURNING id AS "Value"
            """).ToListAsync()).Single();

        if (creationStatus != status) await ChangeStatusAsync(context, id, status);
        return id;
    }

    private static Task<int> ChangeStatusAsync(AppDbContext context, int appointmentId, string status) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE scheduling.appointment
            SET status = {status},
                status_reason = CASE WHEN {status} = 'rejected' THEN 'Motivo de la prueba' ELSE status_reason END
            WHERE id = {appointmentId}
            """);

    private static async Task<int> InsertPatientAsync(AppDbContext context)
    {
        var user = UserMother.WithRole(
            UserRole.Patient, email: Email.Create($"paciente-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
        return await context.Patients.Where(patient => patient.UserId == user.Id)
            .Select(patient => patient.Id).SingleAsync();
    }

    private async Task<IReadOnlyList<BookedInterval>> ListAsync(
        int doctorId, DateTimeOffset from, DateTimeOffset to)
    {
        await using var context = CreateContext();
        return await new ActiveAppointmentReader(context).ListActiveIntervalsAsync(
            doctorId, from, to, CancellationToken.None);
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);
}
