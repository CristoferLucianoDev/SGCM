using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class CompletePatientProfileTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Url = "/api/patients/profile";
    private const string Email = "paciente@correo.com";
    private const string Password = "Secreta123";

    private const string MissingFirstName = "sin_nombre";
    private const string MissingLastName = "sin_apellido";
    private const string MissingPhone = "sin_telefono";
    private const string MissingBirthDate = "sin_fecha_de_nacimiento";
    private const string FutureBirthDate = "fecha_de_nacimiento_futura";
    private const string FirstNameTooLong = "nombre_de_101_caracteres";
    private const string IncompleteEmergencyContact = "contacto_de_emergencia_incompleto";
    private const string ProviderWithoutPolicy = "proveedor_sin_poliza";

    public static TheoryData<string, string> InvalidProfiles => new()
    {
        { MissingFirstName, PatientErrors.FirstNameRequired.Code },
        { MissingLastName, PatientErrors.LastNameRequired.Code },
        { MissingPhone, PatientErrors.PhoneRequired.Code },
        { MissingBirthDate, PatientErrors.InvalidBirthDate.Code },
        { FutureBirthDate, PatientErrors.InvalidBirthDate.Code },
        { FirstNameTooLong, PatientErrors.FirstNameTooLong.Code },
        { IncompleteEmergencyContact, PatientErrors.IncompleteEmergencyContact.Code },
        { ProviderWithoutPolicy, PatientErrors.PolicyNumberRequired.Code }
    };

    private DateOnly Today => DateOnly.FromDateTime(Clock.UtcNow.UtcDateTime);

    [Fact]
    public async Task Paciente_con_paso_pendiente_completa_su_perfil_y_recibe_tokens_sin_paso_pendiente()
    {
        var login = await LoginNewPatientAsync();
        var insuranceProviderId = await InsertInsuranceProviderAsync();

        using var response = await PostProfileAsync(CompletePatientProfileRequestMother.WithAllData(insuranceProviderId));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var tokens = await response.ReadLoginAsync();
        tokens.RequiresTwoFactor.ShouldBeFalse();
        tokens.PendingStep.ShouldBeNull();
        tokens.AccessToken.ShouldNotBe(login.AccessToken);
        tokens.RefreshToken.ShouldNotBeNullOrEmpty();
        ReadAccessToken(tokens).TryGetClaim(AppClaims.PendingStep, out _).ShouldBeFalse();

        var user = await FindUserAsync(Email);
        var patient = await FindPatientAsync(user!.Id);
        patient.ShouldNotBeNull();
        patient.FirstName.ShouldBe(PatientMother.DefaultFirstName);
        patient.LastName.ShouldBe(PatientMother.DefaultLastName);
        patient.BirthDate.ShouldBe(PatientMother.DefaultBirthDate);
        patient.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        patient.Address.ShouldBe(PatientMother.DefaultAddress);
        patient.EmergencyContact.ShouldNotBeNull();
        patient.EmergencyContact.Name.ShouldBe(PatientMother.DefaultEmergencyContactName);
        patient.EmergencyContact.PhoneNumber.ShouldBe(PatientMother.DefaultEmergencyContactPhone);
        patient.InsuranceProviderId.ShouldBe(insuranceProviderId);
        patient.PolicyNumber.ShouldBe(PatientMother.DefaultPolicyNumber);
        user.Sessions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Los_datos_opcionales_pueden_omitirse()
    {
        await LoginNewPatientAsync();

        using var response = await PostProfileAsync(CompletePatientProfileRequestMother.WithRequiredData());

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var user = await FindUserAsync(Email);
        var patient = await FindPatientAsync(user!.Id);
        patient.ShouldNotBeNull();
        patient.Address.ShouldBeNull();
        patient.EmergencyContact.ShouldBeNull();
        patient.InsuranceProviderId.ShouldBeNull();
        patient.PolicyNumber.ShouldBeNull();
    }

    [Fact]
    public async Task El_siguiente_inicio_de_sesion_ya_no_tiene_paso_pendiente()
    {
        await LoginNewPatientAsync();
        using var response = await PostProfileAsync(CompletePatientProfileRequestMother.WithRequiredData());
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);

        var login = await LoginSuccessfullyAsync(Email, Password);

        login.PendingStep.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public async Task Un_perfil_invalido_responde_400_y_no_se_guarda(string caso, string errorCode)
    {
        await LoginNewPatientAsync();
        var insuranceProviderId = await InsertInsuranceProviderAsync();

        using var response = await PostProfileAsync(InvalidRequest(caso, insuranceProviderId));

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(errorCode);
        (await CountPatientsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Se_aceptan_las_longitudes_maximas()
    {
        await LoginNewPatientAsync();
        var insuranceProviderId = await InsertInsuranceProviderAsync();
        var request = CompletePatientProfileRequestMother.WithAllData(insuranceProviderId) with
        {
            FirstName = new string('A', 100),
            LastName = new string('A', 100),
            PhoneNumber = new string('1', 20),
            Address = new string('A', 250),
            EmergencyContactName = new string('A', 150),
            EmergencyContactPhone = new string('1', 20),
            PolicyNumber = new string('A', 50)
        };

        using var response = await PostProfileAsync(request);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await CountPatientsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Un_proveedor_inactivo_responde_400_y_no_se_guarda()
    {
        await LoginNewPatientAsync();
        var insuranceProviderId = await InsertInsuranceProviderAsync(isActive: false);

        using var response = await PostProfileAsync(CompletePatientProfileRequestMother.WithAllData(insuranceProviderId));

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(PatientErrors.InvalidInsuranceProvider.Code);
        (await CountPatientsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Un_proveedor_inexistente_responde_400_y_no_se_guarda()
    {
        await LoginNewPatientAsync();

        using var response = await PostProfileAsync(CompletePatientProfileRequestMother.WithAllData(int.MaxValue));

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(PatientErrors.InvalidInsuranceProvider.Code);
        (await CountPatientsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Un_perfil_ya_completado_responde_409_sin_crear_otro()
    {
        await LoginNewPatientAsync();
        using var first = await PostProfileAsync(CompletePatientProfileRequestMother.WithRequiredData());
        var tokens = await first.ReadLoginAsync();
        Authorize(tokens.AccessToken);

        using var response = await PostProfileAsync(CompletePatientProfileRequestMother.WithRequiredData());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(PatientErrors.ProfileAlreadyExists.Code);
        (await CountPatientsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Con_el_paso_pendiente_otra_funcion_responde_403_hasta_completar_el_perfil()
    {
        var login = await LoginNewPatientAsync();
        using var protectedClient = await CreateProtectedEndpointClientAsync(login.AccessToken);

        using var blocked = await protectedClient.GetAsync(ProtectedTestController.RoutePath);
        await blocked.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);

        using var profile = await PostProfileAsync(CompletePatientProfileRequestMother.WithRequiredData());
        var tokens = await profile.ReadLoginAsync();
        protectedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var allowed = await protectedClient.GetAsync(ProtectedTestController.RoutePath);
        await allowed.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Un_paciente_con_otro_paso_pendiente_no_puede_completar_el_perfil()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var token = Factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(1, UserRole.Patient, 1, PendingStep.ChangeTemporaryPassword);
        Authorize(token.Value);

        using var response = await PostProfileAsync(CompletePatientProfileRequestMother.WithRequiredData());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await CountPatientsAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Administrator)]
    public async Task Otros_roles_responden_403(UserRole role)
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var token = Factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(1, role, 1, null);
        Authorize(token.Value);

        using var response = await PostProfileAsync(CompletePatientProfileRequestMother.WithRequiredData());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await CountPatientsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        using var response = await PostProfileAsync(CompletePatientProfileRequestMother.WithRequiredData());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await CountPatientsAsync()).ShouldBe(0);
    }

    // Los access tokens vencen según el reloj real, así que la app emite con la hora actual.
    private async Task<LoginResponse> LoginNewPatientAsync()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        await CreateConfirmedPatientAsync(Email, Password);

        var login = await LoginSuccessfullyAsync(Email, Password);
        login.PendingStep.ShouldBe(PendingStepClaimValues.PatientProfile);
        Authorize(login.AccessToken);

        return login;
    }

    private CompletePatientProfileRequest InvalidRequest(string caso, int insuranceProviderId)
    {
        var valid = CompletePatientProfileRequestMother.WithRequiredData();

        return caso switch
        {
            MissingFirstName => valid with { FirstName = null! },
            MissingLastName => valid with { LastName = "" },
            MissingPhone => valid with { PhoneNumber = "   " },
            MissingBirthDate => valid with { BirthDate = default },
            FutureBirthDate => valid with { BirthDate = Today.AddDays(1) },
            FirstNameTooLong => valid with { FirstName = new string('A', 101) },
            IncompleteEmergencyContact => valid with { EmergencyContactName = PatientMother.DefaultEmergencyContactName },
            ProviderWithoutPolicy => valid with { InsuranceProviderId = insuranceProviderId },
            _ => throw new ArgumentOutOfRangeException(nameof(caso), caso, "Caso de prueba desconocido.")
        };
    }

    private void Authorize(string? accessToken) =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private Task<HttpResponseMessage> PostProfileAsync(CompletePatientProfileRequest request) =>
        Client.PostAsJsonAsync(Url, request);

    private static async Task<HttpClient> CreateProtectedEndpointClientAsync(string? accessToken)
    {
        var client = (await ProtectedEndpointApp.GetAsync()).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private async Task<int> InsertInsuranceProviderAsync(bool isActive = true)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await TestDataSeeder.InsertInsuranceProviderAsync(context);
        if (!isActive)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE people.insurance_provider SET is_active = false WHERE id = {id}");
        }

        return id;
    }

    private async Task<Patient?> FindPatientAsync(int userId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Patients
            .AsNoTracking().SingleOrDefaultAsync(patient => patient.UserId == userId);
    }

    private async Task<int> CountPatientsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Patients.CountAsync();
    }

    private static JsonWebToken ReadAccessToken(LoginResponse login) =>
        new JsonWebTokenHandler().ReadJsonWebToken(
            login.AccessToken ?? throw new InvalidOperationException("La respuesta no trae access token."));
}
