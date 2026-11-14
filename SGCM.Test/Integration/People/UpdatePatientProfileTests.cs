using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class UpdatePatientProfileTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Url = "/api/patients/me";
    private const string Email = "paciente@correo.com";
    private const string OtherEmail = "otro@correo.com";
    private const string Password = "Secreta123";

    private const string MissingPhone = "sin_telefono";
    private const string PhoneTooLong = "telefono_de_21_caracteres";
    private const string AddressTooLong = "direccion_de_251_caracteres";
    private const string IncompleteEmergencyContact = "contacto_de_emergencia_incompleto";
    private const string ProviderWithoutPolicy = "proveedor_sin_poliza";

    public static TheoryData<string, string> InvalidUpdates => new()
    {
        { MissingPhone, PatientErrors.PhoneRequired.Code },
        { PhoneTooLong, PatientErrors.PhoneTooLong.Code },
        { AddressTooLong, PatientErrors.AddressTooLong.Code },
        { IncompleteEmergencyContact, PatientErrors.IncompleteEmergencyContact.Code },
        { ProviderWithoutPolicy, PatientErrors.PolicyNumberRequired.Code }
    };

    [Fact]
    public async Task El_paciente_actualiza_su_contacto_y_su_seguro()
    {
        var currentProviderId = await InsertInsuranceProviderAsync("Seguros Universal");
        var newProviderId = await InsertInsuranceProviderAsync("Seguros Humano");
        await LoginPatientWithProfileAsync(currentProviderId);

        using var response = await PutProfileAsync(UpdatePatientProfileRequestMother.WithAllData(newProviderId));

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        var patient = await FindMyPatientAsync();
        patient.PhoneNumber.ShouldBe(UpdatePatientProfileRequestMother.NewPhoneNumber);
        patient.Address.ShouldBe(UpdatePatientProfileRequestMother.NewAddress);
        patient.EmergencyContact.ShouldNotBeNull();
        patient.EmergencyContact.Name.ShouldBe(UpdatePatientProfileRequestMother.NewEmergencyContactName);
        patient.EmergencyContact.PhoneNumber.ShouldBe(UpdatePatientProfileRequestMother.NewEmergencyContactPhone);
        patient.InsuranceProviderId.ShouldBe(newProviderId);
        patient.PolicyNumber.ShouldBe(UpdatePatientProfileRequestMother.NewPolicyNumber);
    }

    [Fact]
    public async Task El_paciente_quita_su_seguro_y_su_perfil_queda_sin_proveedor_ni_poliza()
    {
        await LoginPatientWithProfileAsync(await InsertInsuranceProviderAsync("Seguros Universal"));

        using var response = await PutProfileAsync(UpdatePatientProfileRequestMother.WithRequiredData());

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        var patient = await FindMyPatientAsync();
        patient.InsuranceProviderId.ShouldBeNull();
        patient.PolicyNumber.ShouldBeNull();
        patient.Address.ShouldBeNull();
        patient.EmergencyContact.ShouldBeNull();
    }

    [Fact]
    public async Task El_paciente_conserva_un_proveedor_que_ya_fue_desactivado()
    {
        var providerId = await InsertInsuranceProviderAsync("Seguros Universal");
        await LoginPatientWithProfileAsync(providerId);
        await DeactivateInsuranceProviderAsync(providerId);

        using var response = await PutProfileAsync(UpdatePatientProfileRequestMother.WithAllData(providerId));

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        var patient = await FindMyPatientAsync();
        patient.InsuranceProviderId.ShouldBe(providerId);
        patient.PhoneNumber.ShouldBe(UpdatePatientProfileRequestMother.NewPhoneNumber);
    }

    [Fact]
    public async Task Cambiar_a_un_proveedor_inactivo_responde_400_y_no_cambia_nada()
    {
        var currentProviderId = await InsertInsuranceProviderAsync("Seguros Universal");
        var inactiveProviderId = await InsertInsuranceProviderAsync("Seguros Cerrados");
        await DeactivateInsuranceProviderAsync(inactiveProviderId);
        await LoginPatientWithProfileAsync(currentProviderId);

        using var response = await PutProfileAsync(UpdatePatientProfileRequestMother.WithAllData(inactiveProviderId));

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(PatientErrors.InvalidInsuranceProvider.Code);
        await AssertProfileUnchangedAsync(currentProviderId);
    }

    [Fact]
    public async Task Cambiar_a_un_proveedor_inexistente_responde_400_y_no_cambia_nada()
    {
        var currentProviderId = await InsertInsuranceProviderAsync("Seguros Universal");
        await LoginPatientWithProfileAsync(currentProviderId);

        using var response = await PutProfileAsync(UpdatePatientProfileRequestMother.WithAllData(int.MaxValue));

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(PatientErrors.InvalidInsuranceProvider.Code);
        await AssertProfileUnchangedAsync(currentProviderId);
    }

    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public async Task Un_dato_invalido_responde_400_y_no_cambia_nada(string caso, string errorCode)
    {
        var currentProviderId = await InsertInsuranceProviderAsync("Seguros Universal");
        await LoginPatientWithProfileAsync(currentProviderId);

        using var response = await PutProfileAsync(InvalidRequest(caso, currentProviderId));

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(errorCode);
        await AssertProfileUnchangedAsync(currentProviderId);
    }

    [Fact]
    public async Task Se_aceptan_las_longitudes_maximas()
    {
        var providerId = await InsertInsuranceProviderAsync("Seguros Universal");
        await LoginPatientWithProfileAsync(providerId);
        var request = UpdatePatientProfileRequestMother.WithAllData(providerId) with
        {
            PhoneNumber = new string('1', 20),
            Address = new string('A', 250),
            EmergencyContactName = new string('A', 150),
            EmergencyContactPhone = new string('1', 20),
            PolicyNumber = new string('A', 50)
        };

        using var response = await PutProfileAsync(request);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await FindMyPatientAsync()).Address.ShouldBe(new string('A', 250));
    }

    [Fact]
    public async Task Nombre_apellido_y_fecha_de_nacimiento_enviados_se_ignoran()
    {
        await LoginPatientWithProfileAsync(await InsertInsuranceProviderAsync("Seguros Universal"));
        var body = new
        {
            firstName = "Otro",
            lastName = "Nombre",
            birthDate = PatientMother.DefaultBirthDate.AddYears(-10),
            phoneNumber = UpdatePatientProfileRequestMother.NewPhoneNumber
        };

        using var response = await Client.PutAsJsonAsync(Url, body);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        var patient = await FindMyPatientAsync();
        patient.FirstName.ShouldBe(PatientMother.DefaultFirstName);
        patient.LastName.ShouldBe(PatientMother.DefaultLastName);
        patient.BirthDate.ShouldBe(PatientMother.DefaultBirthDate);
        patient.PhoneNumber.ShouldBe(UpdatePatientProfileRequestMother.NewPhoneNumber);
    }

    [Fact]
    public async Task Un_paciente_no_modifica_el_perfil_de_otro()
    {
        await CreateConfirmedPatientAsync(OtherEmail, Password);
        await Factory.InsertPatientProfileAsync(OtherEmail);
        await LoginPatientWithProfileAsync(await InsertInsuranceProviderAsync("Seguros Universal"));

        using var response = await PutProfileAsync(UpdatePatientProfileRequestMother.WithRequiredData());

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        var other = await FindPatientAsync(OtherEmail);
        other.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        (await FindMyPatientAsync()).PhoneNumber.ShouldBe(UpdatePatientProfileRequestMother.NewPhoneNumber);
    }

    [Fact]
    public async Task Un_paciente_con_el_perfil_pendiente_recibe_403()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        await CreateConfirmedPatientAsync(Email, Password);
        var login = await LoginSuccessfullyAsync(Email, Password);
        login.PendingStep.ShouldBe(PendingStepClaimValues.PatientProfile);
        Authorize(login.AccessToken);

        using var response = await PutProfileAsync(UpdatePatientProfileRequestMother.WithRequiredData());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_paciente_sin_perfil_responde_404()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        await CreateConfirmedPatientAsync(Email, Password);
        var user = await FindUserAsync(Email);
        // Token sin paso pendiente para llegar al handler, aunque la cuenta no tenga perfil.
        var token = Factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(user!.Id, UserRole.Patient, 1, null);
        Authorize(token.Value);

        using var response = await PutProfileAsync(UpdatePatientProfileRequestMother.WithRequiredData());

        await response.ShouldHaveStatusAsync(HttpStatusCode.NotFound);
        (await response.ReadErrorCodeAsync()).ShouldBe(PatientErrors.ProfileNotFound.Code);
    }

    [Theory]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Administrator)]
    public async Task Otros_roles_responden_403(UserRole role)
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var token = Factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(1, role, 1, null);
        Authorize(token.Value);

        using var response = await PutProfileAsync(UpdatePatientProfileRequestMother.WithRequiredData());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        using var response = await PutProfileAsync(UpdatePatientProfileRequestMother.WithRequiredData());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    // Registra al paciente, completa su perfil con todos los datos y deja autorizado el cliente
    // con los tokens nuevos, ya sin paso pendiente. Los tokens vencen según el reloj real.
    private async Task LoginPatientWithProfileAsync(int insuranceProviderId)
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        await CreateConfirmedPatientAsync(Email, Password);
        var login = await LoginSuccessfullyAsync(Email, Password);
        Authorize(login.AccessToken);

        using var profile = await Client.PostAsJsonAsync(
            "/api/patients/profile", CompletePatientProfileRequestMother.WithAllData(insuranceProviderId));
        await profile.ShouldHaveStatusAsync(HttpStatusCode.OK);
        Authorize((await profile.ReadLoginAsync()).AccessToken);
    }

    private static UpdatePatientProfileRequest InvalidRequest(string caso, int insuranceProviderId)
    {
        var valid = UpdatePatientProfileRequestMother.WithAllData(insuranceProviderId);

        return caso switch
        {
            MissingPhone => valid with { PhoneNumber = "   " },
            PhoneTooLong => valid with { PhoneNumber = new string('1', 21) },
            AddressTooLong => valid with { Address = new string('A', 251) },
            IncompleteEmergencyContact => valid with { EmergencyContactPhone = null },
            ProviderWithoutPolicy => valid with { PolicyNumber = null },
            _ => throw new ArgumentOutOfRangeException(nameof(caso), caso, "Caso de prueba desconocido.")
        };
    }

    private async Task AssertProfileUnchangedAsync(int insuranceProviderId)
    {
        var patient = await FindMyPatientAsync();
        patient.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        patient.Address.ShouldBe(PatientMother.DefaultAddress);
        patient.EmergencyContact.ShouldNotBeNull();
        patient.EmergencyContact.Name.ShouldBe(PatientMother.DefaultEmergencyContactName);
        patient.InsuranceProviderId.ShouldBe(insuranceProviderId);
        patient.PolicyNumber.ShouldBe(PatientMother.DefaultPolicyNumber);
    }

    private void Authorize(string? accessToken) =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private Task<HttpResponseMessage> PutProfileAsync(UpdatePatientProfileRequest request) =>
        Client.PutAsJsonAsync(Url, request);

    private async Task<int> InsertInsuranceProviderAsync(string name)
    {
        using var scope = Factory.Services.CreateScope();
        return await TestDataSeeder.InsertInsuranceProviderAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), name);
    }

    private async Task DeactivateInsuranceProviderAsync(int insuranceProviderId)
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.insurance_provider SET is_active = false WHERE id = {insuranceProviderId}");
    }

    private Task<Patient> FindMyPatientAsync() => FindPatientAsync(Email);

    private async Task<Patient> FindPatientAsync(string email)
    {
        var user = await FindUserAsync(email)
            ?? throw new InvalidOperationException($"No hay un usuario con el correo {email}.");

        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Patients
            .AsNoTracking().SingleAsync(patient => patient.UserId == user.Id);
    }
}
