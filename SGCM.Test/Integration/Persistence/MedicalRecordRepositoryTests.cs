using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.Clinical;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

/// <summary>
/// Pruebas de integración del mapeo y del repositorio de solo inserción del registro clínico contra PostgreSQL, y de
/// su registro en la infraestructura. Las filas se leen con SQL para comprobar lo que realmente quedó en cada columna.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MedicalRecordRepositoryTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private static readonly DateTimeOffset RecordedAt = ClinicalPersistenceData.PastStartsAt.AddMinutes(30);
    private static readonly DateTimeOffset FutureStartsAt = new(2030, 3, 10, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Guarda_la_version_en_cada_columna()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "confirmed");
        var record = MedicalRecord.Create(
            seed.AppointmentId, seed.PatientId, Content("Faringitis", "Reposo", "Control en 7 días"),
            seed.DoctorUserId, RecordedAt);

        await SaveAsync(context, record);

        record.Id.ShouldBeGreaterThan(0);
        (await ReadRowsAsync(seed.AppointmentId)).ShouldHaveSingleItem().ShouldBe(new RecordRow(
            record.Id, seed.AppointmentId, seed.PatientId, "Faringitis", "Reposo", "Control en 7 días", RecordedAt,
            seed.DoctorUserId, UpdatedAt: null, UpdatedBy: null));
    }

    [Fact]
    public async Task Las_partes_vacias_se_guardan_como_null()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "completed");
        var record = MedicalRecord.Create(
            seed.AppointmentId, seed.PatientId, Content(null, null, "Paciente estable"), seed.DoctorUserId, RecordedAt);

        await SaveAsync(context, record);

        var row = (await ReadRowsAsync(seed.AppointmentId)).ShouldHaveSingleItem();
        row.Diagnosis.ShouldBeNull();
        row.Treatment.ShouldBeNull();
        row.Notes.ShouldBe("Paciente estable");
    }

    [Fact]
    public async Task Corregir_inserta_una_fila_nueva_y_conserva_la_anterior_sin_cambios()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "confirmed");
        var original = MedicalRecord.Create(
            seed.AppointmentId, seed.PatientId, Content("Faringitis"), seed.DoctorUserId, RecordedAt);
        await SaveAsync(context, original);

        await using var correctionContext = CreateContext();
        var current = await new MedicalRecordRepository(correctionContext)
            .GetCurrentAsync(seed.AppointmentId, CancellationToken.None);
        var corrected = current.ShouldNotBeNull()
            .Correct(Content("Amigdalitis", "Antibiótico"), seed.DoctorUserId, RecordedAt.AddHours(1));
        await SaveAsync(correctionContext, corrected);

        var rows = await ReadRowsAsync(seed.AppointmentId);
        rows.Count.ShouldBe(2);
        rows[0].ShouldBe(new RecordRow(
            original.Id, seed.AppointmentId, seed.PatientId, "Faringitis", null, null, RecordedAt, seed.DoctorUserId,
            UpdatedAt: null, UpdatedBy: null));
        rows[1].Id.ShouldBe(corrected.Id);
        rows[1].Diagnosis.ShouldBe("Amigdalitis");
        rows[1].Treatment.ShouldBe("Antibiótico");
    }

    [Fact]
    public async Task La_vigente_es_la_mas_reciente_y_coincide_con_la_vista()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "confirmed");
        var first = MedicalRecord.Create(
            seed.AppointmentId, seed.PatientId, Content("Faringitis"), seed.DoctorUserId, RecordedAt);
        var second = first.Correct(Content("Amigdalitis"), seed.DoctorUserId, RecordedAt.AddHours(1));
        var third = second.Correct(Content("Amigdalitis bacteriana"), seed.DoctorUserId, RecordedAt.AddHours(2));
        // Se insertan desordenadas para que la vigente no dependa del id.
        await SaveAsync(context, third);
        await SaveAsync(context, first);
        await SaveAsync(context, second);

        var current = await GetCurrentAsync(seed.AppointmentId);

        current.ShouldNotBeNull().Id.ShouldBe(third.Id);
        current.Diagnosis.ShouldBe("Amigdalitis bacteriana");
        (await CurrentIdFromViewAsync(seed.AppointmentId)).ShouldBe(third.Id);
    }

    [Fact]
    public async Task Una_cita_sin_registro_no_tiene_version_vigente()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "confirmed");
        var other = await ClinicalPersistenceData.SeedAppointmentAsync(context, "confirmed");
        await SaveAsync(context, MedicalRecord.Create(
            other.AppointmentId, other.PatientId, Content("Gripe"), other.DoctorUserId, RecordedAt));

        (await GetCurrentAsync(seed.AppointmentId)).ShouldBeNull();
    }

    [Fact]
    public async Task La_base_rechaza_el_registro_de_una_cita_que_aun_no_empieza()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "confirmed", FutureStartsAt);
        var record = MedicalRecord.Create(
            seed.AppointmentId, seed.PatientId, Content("Faringitis"), seed.DoctorUserId, RecordedAt);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => SaveAsync(context, record));

        exception.InnerException.ShouldBeOfType<PostgresException>()
            .ConstraintName.ShouldBe("tg_medical_record_appointment_started");
    }

    [Fact]
    public async Task La_base_rechaza_un_paciente_distinto_al_de_la_cita()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "confirmed");
        var otherPatientId = await ClinicalPersistenceData.InsertPatientAsync(context);
        var record = MedicalRecord.Create(
            seed.AppointmentId, otherPatientId, Content("Faringitis"), seed.DoctorUserId, RecordedAt);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => SaveAsync(context, record));

        exception.InnerException.ShouldBeOfType<PostgresException>()
            .ConstraintName.ShouldBe("fk_medical_record_appointment");
    }

    [Fact]
    public void AddInfrastructure_registra_el_repositorio_con_alcance_por_solicitud()
    {
        using var provider = DoctorPersistenceData.Provider("Host=localhost;Database=unused;Username=unused;Password=unused");
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var repository = first.ServiceProvider.GetRequiredService<IMedicalRecordRepository>();

        repository.ShouldBeOfType<MedicalRecordRepository>();
        repository.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IMedicalRecordRepository>());
        repository.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IMedicalRecordRepository>());
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);

    private static MedicalRecordContent Content(string? diagnosis, string? treatment = null, string? notes = null) =>
        MedicalRecordContent.Create(diagnosis, treatment, notes).Value;

    private static async Task SaveAsync(AppDbContext context, MedicalRecord record)
    {
        new MedicalRecordRepository(context).Add(record);
        await context.SaveChangesAsync();
    }

    private async Task<MedicalRecord?> GetCurrentAsync(int appointmentId)
    {
        await using var context = CreateContext();
        return await new MedicalRecordRepository(context).GetCurrentAsync(appointmentId, CancellationToken.None);
    }

    // Las columnas van en snake_case porque SqlQuery aplica la misma convención de nombres que el contexto.
    private async Task<List<RecordRow>> ReadRowsAsync(int appointmentId)
    {
        await using var context = CreateContext();
        return await context.Database.SqlQuery<RecordRow>(
            $"""
            SELECT id, appointment_id, patient_id, diagnosis, treatment, notes, created_at, created_by,
                   updated_at, updated_by
            FROM clinical.medical_record
            WHERE appointment_id = {appointmentId}
            ORDER BY id
            """).ToListAsync();
    }

    private async Task<int> CurrentIdFromViewAsync(int appointmentId)
    {
        await using var context = CreateContext();
        return (await context.Database.SqlQuery<int>(
            $"""
            SELECT id AS "Value" FROM clinical.vw_medical_record_current WHERE appointment_id = {appointmentId}
            """).ToListAsync()).Single();
    }

    private sealed record RecordRow(
        int Id,
        int AppointmentId,
        int PatientId,
        string? Diagnosis,
        string? Treatment,
        string? Notes,
        DateTimeOffset CreatedAt,
        int? CreatedBy,
        DateTimeOffset? UpdatedAt,
        int? UpdatedBy);
}
