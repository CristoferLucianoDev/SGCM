using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

/// <summary>Pruebas de integración del lector del historial médico contra PostgreSQL.</summary>
[Trait("Category", "Integration")]
public sealed class MedicalHistoryReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private static readonly DateTimeOffset Earlier = new(2026, 2, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 3, 10, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lista_las_citas_completadas_y_las_que_tienen_registro_de_la_mas_reciente_a_la_mas_antigua()
    {
        await using var context = CreateContext();
        var completed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "completed", Earlier);
        var recorded = await SeedForPatientAsync(context, completed.PatientId, "confirmed", Later);
        await AddVersionAsync(context, recorded, "Faringitis", Later.AddMinutes(30));
        await SeedForPatientAsync(context, completed.PatientId, "confirmed", Later.AddDays(1));
        await SeedForPatientAsync(context, completed.PatientId, "cancelled", Later.AddDays(2));

        var history = await ListAsync(completed.PatientId);

        history.Select(item => item.AppointmentId).ShouldBe([recorded.AppointmentId, completed.AppointmentId]);
        history[0].Status.ShouldBe(AppointmentStatus.Confirmed);
        history[1].Status.ShouldBe(AppointmentStatus.Completed);
        history[1].Versions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Devuelve_el_medico_la_especialidad_y_las_horas_de_la_cita()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "completed", Earlier);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.doctor SET first_name = ' Ana ', last_name = 'Pérez ' WHERE id = {seed.DoctorId}");
        var specialtyName = await context.Database.SqlQuery<string>(
            $"""
            SELECT s.name AS "Value" FROM scheduling.appointment a JOIN people.specialty s ON s.id = a.specialty_id
            WHERE a.id = {seed.AppointmentId}
            """).SingleAsync();

        var item = (await ListAsync(seed.PatientId)).ShouldHaveSingleItem();

        item.DoctorId.ShouldBe(seed.DoctorId);
        item.DoctorName.ShouldBe("Ana Pérez");
        item.SpecialtyName.ShouldBe(specialtyName);
        item.StartsAt.ShouldBe(Earlier);
        item.EndsAt.ShouldBe(Earlier.AddHours(AppointmentMother.DurationHours));
    }

    [Fact]
    public async Task Las_versiones_van_de_la_mas_reciente_a_la_mas_antigua_sin_depender_del_id()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "completed", Earlier);
        var newest = await AddVersionAsync(context, seed, "Amigdalitis bacteriana", Earlier.AddHours(5));
        var oldest = await AddVersionAsync(context, seed, "Faringitis", Earlier.AddHours(1));
        var middle = await AddVersionAsync(context, seed, "Amigdalitis", Earlier.AddHours(3));

        var versions = (await ListAsync(seed.PatientId)).ShouldHaveSingleItem().Versions;

        versions.Select(version => version.Id).ShouldBe([newest.Id, middle.Id, oldest.Id]);
        versions[0].ShouldBe(new MedicalHistoryVersionData(
            newest.Id, "Amigdalitis bacteriana", null, null, Earlier.AddHours(5)));
    }

    [Fact]
    public async Task No_incluye_citas_de_otros_pacientes()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "completed", Earlier);
        var other = await ClinicalPersistenceData.SeedAppointmentAsync(context, "completed", Later);
        await AddVersionAsync(context, other, "Gripe", Later.AddHours(1));

        (await ListAsync(seed.PatientId)).ShouldHaveSingleItem().AppointmentId.ShouldBe(seed.AppointmentId);
    }

    [Fact]
    public async Task Un_paciente_sin_citas_en_el_historial_devuelve_una_lista_vacia()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "pending", Later);

        (await ListAsync(seed.PatientId)).ShouldBeEmpty();
    }

    [Fact]
    public void AddInfrastructure_registra_el_lector_con_alcance_por_solicitud()
    {
        using var provider = DoctorPersistenceData.Provider("Host=localhost;Database=unused;Username=unused;Password=unused");
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var reader = first.ServiceProvider.GetRequiredService<IMedicalHistoryReader>();

        reader.ShouldBeOfType<MedicalHistoryReader>();
        reader.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IMedicalHistoryReader>());
        reader.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IMedicalHistoryReader>());
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);

    private async Task<IReadOnlyList<MedicalHistoryAppointmentData>> ListAsync(int patientId)
    {
        await using var context = CreateContext();
        return await new MedicalHistoryReader(context).ListAsync(patientId, CancellationToken.None);
    }

    // Otra cita del mismo paciente, con un médico nuevo para que los horarios no se crucen.
    private static Task<ClinicalAppointmentSeed> SeedForPatientAsync(
        AppDbContext context, int patientId, string status, DateTimeOffset startsAt) =>
        ClinicalPersistenceData.SeedAppointmentAsync(context, status, startsAt, patientId);

    private static async Task<MedicalRecord> AddVersionAsync(
        AppDbContext context, ClinicalAppointmentSeed seed, string diagnosis, DateTimeOffset createdAt)
    {
        var record = MedicalRecord.Create(
            seed.AppointmentId, seed.PatientId, MedicalRecordContent.Create(diagnosis, null, null).Value,
            seed.DoctorUserId, createdAt);
        context.Add(record);
        await context.SaveChangesAsync();
        return record;
    }
}
