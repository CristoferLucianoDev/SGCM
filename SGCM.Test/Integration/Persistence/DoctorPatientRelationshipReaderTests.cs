using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

/// <summary>Pruebas de integración del lector de la relación entre médico y paciente contra PostgreSQL.</summary>
[Trait("Category", "Integration")]
public sealed class DoctorPatientRelationshipReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Theory]
    [InlineData("pending")]
    [InlineData("confirmed")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    [InlineData("rejected")]
    public async Task Una_cita_en_cualquier_estado_es_relacion(string status)
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, status);

        (await HasAsync(seed.DoctorId, seed.PatientId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Sin_citas_entre_ambos_no_hay_relacion()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "confirmed");
        var other = await ClinicalPersistenceData.SeedAppointmentAsync(context, "confirmed");

        (await HasAsync(seed.DoctorId, other.PatientId)).ShouldBeFalse();
        (await HasAsync(other.DoctorId, seed.PatientId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Un_paciente_o_un_medico_inexistente_no_tiene_relacion()
    {
        await using var context = CreateContext();
        var seed = await ClinicalPersistenceData.SeedAppointmentAsync(context, "confirmed");

        (await HasAsync(seed.DoctorId, int.MaxValue)).ShouldBeFalse();
        (await HasAsync(int.MaxValue, seed.PatientId)).ShouldBeFalse();
    }

    [Fact]
    public void AddInfrastructure_registra_el_lector_con_alcance_por_solicitud()
    {
        using var provider = DoctorPersistenceData.Provider("Host=localhost;Database=unused;Username=unused;Password=unused");
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var reader = first.ServiceProvider.GetRequiredService<IDoctorPatientRelationshipReader>();

        reader.ShouldBeOfType<DoctorPatientRelationshipReader>();
        reader.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IDoctorPatientRelationshipReader>());
        reader.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IDoctorPatientRelationshipReader>());
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);

    private async Task<bool> HasAsync(int doctorId, int patientId)
    {
        await using var context = CreateContext();
        return await new DoctorPatientRelationshipReader(context)
            .HasAppointmentWithPatientAsync(doctorId, patientId, CancellationToken.None);
    }
}
