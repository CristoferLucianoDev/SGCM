using Microsoft.EntityFrameworkCore;
using Npgsql;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public class PatientPersistenceTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task Patient_persiste_y_recupera_todos_sus_datos()
    {
        var userId = await SaveUserAsync();
        var insuranceProviderId = await InsertInsuranceProviderAsync();
        var patient = PatientMother.WithAllData(userId, insuranceProviderId);

        var loaded = await SaveAndReloadAsync(patient);

        loaded.UserId.ShouldBe(userId);
        loaded.FirstName.ShouldBe(PatientMother.DefaultFirstName);
        loaded.LastName.ShouldBe(PatientMother.DefaultLastName);
        loaded.BirthDate.ShouldBe(PatientMother.DefaultBirthDate);
        loaded.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        loaded.Address.ShouldBe(PatientMother.DefaultAddress);
        loaded.EmergencyContact.ShouldNotBeNull();
        loaded.EmergencyContact.Name.ShouldBe(PatientMother.DefaultEmergencyContactName);
        loaded.EmergencyContact.PhoneNumber.ShouldBe(PatientMother.DefaultEmergencyContactPhone);
        loaded.InsuranceProviderId.ShouldBe(insuranceProviderId);
        loaded.PolicyNumber.ShouldBe(PatientMother.DefaultPolicyNumber);
        loaded.IsActive.ShouldBeTrue();
        loaded.CreatedAt.ShouldBe(UserMother.Now);
        loaded.CreatedBy.ShouldBe(userId);
    }

    [Fact]
    public async Task Patient_sin_datos_opcionales_recupera_el_contacto_de_emergencia_como_null()
    {
        var userId = await SaveUserAsync();

        var loaded = await SaveAndReloadAsync(PatientMother.WithRequiredData(userId));

        loaded.Address.ShouldBeNull();
        loaded.EmergencyContact.ShouldBeNull();
        loaded.InsuranceProviderId.ShouldBeNull();
        loaded.PolicyNumber.ShouldBeNull();
    }

    [Fact]
    public async Task Patient_inactivo_persiste_el_estado_explicito()
    {
        var userId = await SaveUserAsync();
        var patient = PatientMother.WithRequiredData(userId);
        patient.Deactivate().IsSuccess.ShouldBeTrue();

        var loaded = await SaveAndReloadAsync(patient);

        loaded.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Patient_persiste_las_longitudes_maximas()
    {
        var userId = await SaveUserAsync();
        var insuranceProviderId = await InsertInsuranceProviderAsync();
        var patient = Patient.Create(
            userId,
            new string('A', 100),
            new string('B', 100),
            PatientMother.DefaultBirthDate,
            new string('1', 20),
            new string('C', 250),
            new string('D', 150),
            new string('2', 20),
            insuranceProviderId,
            new string('E', 50),
            UserMother.Now).Value;

        var loaded = await SaveAndReloadAsync(patient);

        loaded.FirstName.ShouldBe(new string('A', 100));
        loaded.LastName.ShouldBe(new string('B', 100));
        loaded.PhoneNumber.ShouldBe(new string('1', 20));
        loaded.Address.ShouldBe(new string('C', 250));
        loaded.EmergencyContact.ShouldNotBeNull();
        loaded.EmergencyContact.Name.ShouldBe(new string('D', 150));
        loaded.EmergencyContact.PhoneNumber.ShouldBe(new string('2', 20));
        loaded.PolicyNumber.ShouldBe(new string('E', 50));
    }

    [Fact]
    public async Task La_base_rechaza_un_proveedor_de_seguros_sin_numero_de_poliza()
    {
        var userId = await SaveUserAsync();
        var insuranceProviderId = await InsertInsuranceProviderAsync();
        await using var context = CreateContext();

        // Se inserta con SQL para saltar la validación del dominio y probar la última defensa.
        var exception = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO people.patient (user_id, first_name, last_name, birth_date, phone_number, insurance_provider_id)
            VALUES ({userId}, {PatientMother.DefaultFirstName}, {PatientMother.DefaultLastName},
                    {PatientMother.DefaultBirthDate}, {PatientMother.DefaultPhoneNumber}, {insuranceProviderId})
            """));

        exception.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.ShouldBe("ck_patient_policy_number");
    }

    [Fact]
    public async Task La_base_rechaza_un_segundo_perfil_para_el_mismo_usuario()
    {
        var userId = await SaveUserAsync();
        await SaveAndReloadAsync(PatientMother.WithRequiredData(userId));
        await using var context = CreateContext();
        context.Patients.Add(PatientMother.WithRequiredData(userId));

        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());

        var postgres = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("uq_patient_user_id");
    }

    private async Task<Patient> SaveAndReloadAsync(Patient patient)
    {
        await using (var context = CreateContext())
        {
            context.Patients.Add(patient);
            await context.SaveChangesAsync();
        }

        patient.Id.ShouldBeGreaterThan(0);

        await using var reloadContext = CreateContext();
        return await reloadContext.Patients.SingleAsync(value => value.Id == patient.Id);
    }

    private async Task<int> SaveUserAsync()
    {
        // El trigger de la tabla exige que el perfil pertenezca a una cuenta con rol de paciente.
        var user = UserMother.Confirmed();
        user.Role.ShouldBe(UserRole.Patient);

        await using var context = CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> InsertInsuranceProviderAsync()
    {
        await using var context = CreateContext();
        return await TestDataSeeder.InsertInsuranceProviderAsync(context);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}
