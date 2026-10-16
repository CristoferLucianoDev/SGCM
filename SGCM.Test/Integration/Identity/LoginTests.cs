using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Identity;
using DomainPassword = SGCM.Data.Domain.Identity.Users.Password;

namespace SGCM.Test.Integration.Identity;

[Trait("Category", "Integration")]
public sealed class LoginTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Email = "paciente@correo.com";
    private const string DoctorEmail = "medico@correo.com";
    private const string AdministratorEmail = "admin@correo.com";
    private const string UnknownEmail = "desconocido@correo.com";
    private const string Password = "Secreta123";
    private const string WrongPassword = "Incorrecta123";
    private const string InvalidEmailFormat = "no-es-un-correo";
    private const int LowIpLimit = 3;

    // El limitador por IP usa el reloj real, no Clock: una ventana de un día evita que se
    // renueve a mitad de la prueba en una máquina lenta.
    private const int LongIpWindowSeconds = 24 * 60 * 60;

    private const string UnknownEmailCase = "correo_inexistente";
    private const string WrongPasswordCase = "contrasena_incorrecta";
    private const string PendingAccountCase = "cuenta_pendiente";
    private const string InactiveAccountCase = "cuenta_inactiva";

    // Límite por correo y vigencia del refresh token según el PBI.
    private const int MaxFailedAttemptsPerEmail = 5;
    private static readonly TimeSpan EmailWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    public static TheoryData<string, string, string> InvalidLogins => new()
    {
        { InvalidEmailFormat, Password, CommonErrors.InvalidEmail.Code },
        { Email, new string('a', DomainPassword.MaxLength + 1), UserErrors.PasswordTooLong.Code }
    };

    [Fact]
    public async Task Login_con_credenciales_validas_entrega_los_tokens_y_abre_la_sesion()
    {
        await CreateConfirmedPatientAsync(Email, Password);

        var response = await LoginAsync(Email, Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = await response.ReadLoginAsync();
        login.RequiresTwoFactor.ShouldBeFalse();
        login.AccessToken.ShouldNotBeNullOrEmpty();
        login.RefreshToken.ShouldNotBeNullOrEmpty();
        login.RefreshTokenExpiresAt.ShouldBe(Clock.UtcNow + RefreshTokenLifetime);

        var user = await FindUserAsync(Email);
        var session = user!.Sessions.ShouldHaveSingleItem();
        session.IsActiveAt(Clock.UtcNow).ShouldBeTrue();
        session.DeviceName.ShouldBe(UserAgent);

        var accessToken = ReadAccessToken(login);
        accessToken.GetClaim(AppClaims.UserId).Value.ShouldBe(user.Id.ToString(CultureInfo.InvariantCulture));
        accessToken.GetClaim(AppClaims.Role).Value.ShouldBe(nameof(UserRole.Patient));
        accessToken.GetClaim(AppClaims.SessionId).Value.ShouldBe(session.Id.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(UnknownEmailCase)]
    [InlineData(WrongPasswordCase)]
    [InlineData(PendingAccountCase)]
    [InlineData(InactiveAccountCase)]
    public async Task Login_rechazado_responde_igual_en_todos_los_casos(string caso)
    {
        var (email, password) = await PrepareRejectedLoginAsync(caso);

        var response = await LoginAsync(email, password);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe(UserErrors.InvalidCredentials.Code);
        problem.GetProperty("title").GetString().ShouldBe(UserErrors.InvalidCredentials.Description);
        (await CountSessionsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Login_con_2FA_activo_pide_el_segundo_factor_sin_emitir_tokens()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        await Factory.EnableTwoFactorAsync(Email);

        var response = await LoginAsync(Email, Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = await response.ReadLoginAsync();
        login.RequiresTwoFactor.ShouldBeTrue();
        login.AccessToken.ShouldBeNull();
        login.AccessTokenExpiresAt.ShouldBeNull();
        login.RefreshToken.ShouldBeNull();
        login.RefreshTokenExpiresAt.ShouldBeNull();
        login.PendingStep.ShouldBeNull();
        (await FindUserAsync(Email))!.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Login_de_un_paciente_sin_perfil_indica_completarlo()
    {
        await CreateConfirmedPatientAsync(Email, Password);

        var login = await LoginSuccessfullyAsync(Email, Password);

        login.PendingStep.ShouldBe(PendingStepClaimValues.PatientProfile);
        ReadAccessToken(login).GetClaim(AppClaims.PendingStep).Value.ShouldBe(PendingStepClaimValues.PatientProfile);
    }

    [Fact]
    public async Task Login_de_un_paciente_con_perfil_no_tiene_paso_pendiente()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        await Factory.InsertPatientProfileAsync(Email);

        var login = await LoginSuccessfullyAsync(Email, Password);

        login.PendingStep.ShouldBeNull();
        ReadAccessToken(login).TryGetClaim(AppClaims.PendingStep, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Login_de_un_medico_con_contrasena_temporal_indica_cambiarla()
    {
        await SaveUserAsync(UserRole.Doctor, DoctorEmail, Password, mustChangePassword: true);

        var login = await LoginSuccessfullyAsync(DoctorEmail, Password);

        login.PendingStep.ShouldBe(PendingStepClaimValues.ChangeTemporaryPassword);
        ReadAccessToken(login).GetClaim(AppClaims.Role).Value.ShouldBe(nameof(UserRole.Doctor));
    }

    [Fact]
    public async Task Login_de_un_medico_sin_perfil_indica_completarlo()
    {
        await SaveUserAsync(UserRole.Doctor, DoctorEmail, Password, mustChangePassword: false);

        var login = await LoginSuccessfullyAsync(DoctorEmail, Password);

        login.PendingStep.ShouldBe(PendingStepClaimValues.DoctorProfile);
    }

    [Fact]
    public async Task Login_de_un_administrador_no_tiene_paso_pendiente()
    {
        await SaveUserAsync(UserRole.Administrator, AdministratorEmail, Password, mustChangePassword: false);

        var login = await LoginSuccessfullyAsync(AdministratorEmail, Password);

        login.PendingStep.ShouldBeNull();
    }

    [Fact]
    public async Task Login_al_superar_el_limite_por_correo_se_rechaza_aunque_la_contrasena_sea_correcta()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        await FailLoginAsync(Email, MaxFailedAttemptsPerEmail);

        var response = await LoginAsync(Email, Password);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.TooManyLoginAttempts.Code);
    }

    [Fact]
    public async Task El_limite_por_correo_se_libera_al_terminar_la_ventana()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        await FailLoginAsync(Email, MaxFailedAttemptsPerEmail);
        await (await LoginAsync(Email, Password)).ShouldHaveStatusAsync(HttpStatusCode.TooManyRequests);
        Clock.UtcNow += EmailWindow;

        var response = await LoginAsync(Email, Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Un_login_correcto_reinicia_el_contador_de_intentos()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        await FailLoginAsync(Email, MaxFailedAttemptsPerEmail - 1);
        await LoginSuccessfullyAsync(Email, Password);
        await FailLoginAsync(Email, MaxFailedAttemptsPerEmail - 1);

        var response = await LoginAsync(Email, Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Un_login_con_2FA_activo_tambien_reinicia_el_contador()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        await Factory.EnableTwoFactorAsync(Email);
        await FailLoginAsync(Email, MaxFailedAttemptsPerEmail - 1);
        (await LoginSuccessfullyAsync(Email, Password)).RequiresTwoFactor.ShouldBeTrue();
        await FailLoginAsync(Email, MaxFailedAttemptsPerEmail - 1);

        var response = await LoginAsync(Email, Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task El_limite_por_correo_tambien_aplica_a_correos_inexistentes()
    {
        // Así el límite no revela qué correos existen: un correo sin cuenta se bloquea igual.
        await FailLoginAsync(UnknownEmail, MaxFailedAttemptsPerEmail);

        var response = await LoginAsync(UnknownEmail, Password);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.TooManyLoginAttempts.Code);
    }

    [Fact]
    public async Task Login_al_superar_el_limite_por_IP_se_rechaza()
    {
        await using var factory = new SgcmApplicationFactory(
            Database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["LoginLimits:MaxRequestsPerIp"] = LowIpLimit.ToString(CultureInfo.InvariantCulture),
                ["LoginLimits:IpWindowSeconds"] = LongIpWindowSeconds.ToString(CultureInfo.InvariantCulture)
            });
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < LowIpLimit; attempt++)
        {
            await (await LoginAsync(UnknownEmail, Password, client)).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        }

        var response = await LoginAsync(UnknownEmail, Password, client);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await response.ReadErrorCodeAsync()).ShouldBe(CommonErrors.TooManyRequests.Code);
        response.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(InvalidLogins))]
    public async Task Login_con_datos_invalidos_responde_400(string email, string password, string expectedCode)
    {
        var response = await LoginAsync(email, password);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(expectedCode);
    }

    private async Task<(string Email, string Password)> PrepareRejectedLoginAsync(string caso)
    {
        switch (caso)
        {
            case UnknownEmailCase:
                return (UnknownEmail, Password);
            case WrongPasswordCase:
                await CreateConfirmedPatientAsync(Email, Password);
                return (Email, WrongPassword);
            case PendingAccountCase:
                await RegisterPatientAsync(Email, Password);
                return (Email, Password);
            case InactiveAccountCase:
                await CreateConfirmedPatientAsync(Email, Password);
                await Factory.UpdateUserAsync(Email, user => user.Deactivate().IsSuccess.ShouldBeTrue());
                return (Email, Password);
            default:
                throw new ArgumentOutOfRangeException(nameof(caso), caso, "Caso de prueba desconocido.");
        }
    }

    private async Task FailLoginAsync(string email, int attempts)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            await (await LoginAsync(email, WrongPassword)).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        }
    }

    private static JsonWebToken ReadAccessToken(LoginResponse login) =>
        new JsonWebTokenHandler().ReadJsonWebToken(
            login.AccessToken ?? throw new InvalidOperationException("La respuesta no trae access token."));

    private async Task<int> CountSessionsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM app.user_session")
            .SingleAsync();
    }
}
