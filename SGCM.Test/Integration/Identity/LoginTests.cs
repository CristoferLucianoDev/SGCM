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
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Identity;
using DomainPassword = SGCM.Data.Domain.Identity.Users.Password;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using SGCM.Web.Contracts.Common;

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

    [Theory]
    [InlineData(ClientTypeHeader.Web, true, false, "change_password", false)]
    [InlineData(ClientTypeHeader.Mobile, true, false, "change_password", true)]
    [InlineData(ClientTypeHeader.Web, false, false, "doctor_profile", false)]
    [InlineData(ClientTypeHeader.Mobile, false, false, "doctor_profile", true)]
    [InlineData(ClientTypeHeader.Web, false, true, null, false)]
    [InlineData(ClientTypeHeader.Mobile, false, true, null, false)]
    [InlineData(null, true, false, "change_password", false)]
    [InlineData(null, false, false, "doctor_profile", false)]
    public async Task Cliente_declarado_Doctor_conserva_JWT_sesion_y_contrato(
        string? header, bool temporary, bool hasProfile, string? step, bool notice)
    {
        await PrepareClientDoctorAsync(temporary, hasProfile);
        // Dos logins reales: al menos una sesión tendrá id distinto al usuario, sin depender
        // de las secuencias que otras pruebas hayan consumido. Cada login debe añadir solo una.
        var distinctSessionObserved = false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var previousIds = (await FindUserAsync(DoctorEmail))!.Sessions.Select(s => s.Id).ToArray();
            using var response = await ClientLoginAsync(DoctorEmail, Password, header is null ? null : [header]);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            AssertClientJson(json, notice, step);
            var login = json.Deserialize<LoginResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            login.RequiresWebFirstAccess.ShouldBe(notice);
            login.RequiresTwoFactor.ShouldBeFalse();
            login.AccessToken.ShouldNotBeNullOrWhiteSpace();
            login.RefreshToken.ShouldNotBeNullOrWhiteSpace();
            var user = (await FindUserAsync(DoctorEmail))!;
            user.MustChangePassword.ShouldBe(temporary);
            user.Sessions.Count.ShouldBe(previousIds.Length + 1);
            var session = user.Sessions.Where(s => !previousIds.Contains(s.Id)).ShouldHaveSingleItem();
            session.IsActiveAt(Clock.UtcNow).ShouldBeTrue();
            session.DeviceName.ShouldBe(ClientTestAgent);
            distinctSessionObserved |= user.Id != session.Id;
            session.ExpiresAt.ShouldBe(login.RefreshTokenExpiresAt!.Value);
            var jwtOptions = Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
            session.ExpiresAt.ShouldBe(Clock.UtcNow.AddDays(jwtOptions.RefreshTokenDays));
            login.AccessTokenExpiresAt.ShouldBe(Clock.UtcNow.AddMinutes(jwtOptions.AccessTokenMinutes));
            string.Equals(session.RefreshTokenHash,
                Factory.Services.GetRequiredService<ISecureTokenGenerator>().Hash(login.RefreshToken!), StringComparison.Ordinal).ShouldBeTrue();
            var jwt = ReadAccessToken(login);
            jwt.ValidTo.ShouldBe(login.AccessTokenExpiresAt!.Value.UtcDateTime);
            jwt.GetClaim(AppClaims.UserId).Value.ShouldBe(user.Id.ToString(CultureInfo.InvariantCulture));
            jwt.GetClaim(AppClaims.SessionId).Value.ShouldBe(session.Id.ToString(CultureInfo.InvariantCulture));
            jwt.GetClaim(AppClaims.Role).Value.ShouldBe(nameof(UserRole.Doctor));
            if (step is null) jwt.TryGetClaim(AppClaims.PendingStep, out _).ShouldBeFalse();
            else jwt.GetClaim(AppClaims.PendingStep).Value.ShouldBe(step);
            string[] claims = ["iss", "aud", "iat", "nbf", "exp", "sub", "sid", "role", "jti"];
            jwt.Claims.Select(c => c.Type).Distinct().Order()
                .ShouldBe((step is null ? claims : claims.Append("pending_step")).Order());
            var validation = await new JsonWebTokenHandler().ValidateTokenAsync(login.AccessToken!, Factory.Services
                .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters);
            validation.IsValid.ShouldBeTrue();
            if (step is not null)
            {
                var before = await ClientStateSnapshotAsync();
                using var blockedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/doctors/me/specialties/11");
                blockedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
                blockedRequest.Headers.Add(ClientTypeHeader.Name, ClientTypeHeader.Web);
                using var blocked = await Client.SendAsync(blockedRequest);
                blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
                string.Equals(await ClientStateSnapshotAsync(), before, StringComparison.Ordinal).ShouldBeTrue();
            }
        }
        distinctSessionObserved.ShouldBeTrue();
    }

    [Theory]
    [InlineData(UserRole.Patient, false, "patient_profile")]
    [InlineData(UserRole.Patient, true, "change_password")]
    [InlineData(UserRole.Administrator, false, null)]
    public async Task Cliente_declarado_mobile_no_impone_aviso_Doctor_a_otros_roles(
        UserRole role, bool temporary, string? step)
    {
        SetClientClock();
        await SaveUserAsync(role, Email, Password, temporary);
        using var response = await ClientLoginAsync(Email, Password, [ClientTypeHeader.Mobile]);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        AssertClientJson(json, notice: false, step);
        var login = json.Deserialize<LoginResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var jwt = ReadAccessToken(login);
        jwt.GetClaim(AppClaims.Role).Value.ShouldBe(role.ToString());
        var user = (await FindUserAsync(Email))!;
        user.Sessions.ShouldHaveSingleItem().Id.ToString(CultureInfo.InvariantCulture)
            .ShouldBe(jwt.GetClaim(AppClaims.SessionId).Value);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("web,mobile")]
    [InlineData("mobile,web")]
    [InlineData(ClientTypeHeader.Web, ClientTypeHeader.Web)]
    [InlineData(ClientTypeHeader.Mobile, ClientTypeHeader.Mobile)]
    [InlineData(ClientTypeHeader.Web, ClientTypeHeader.Mobile)]
    public async Task Cliente_declarado_invalido_no_crea_sesiones_ni_cambia_datos(params string[] headers)
    {
        await PrepareClientDoctorAsync();
        // Una sesión previa evita que comprobar solo cero sesiones oculte un reemplazo accidental.
        await LoginSuccessfullyAsync(DoctorEmail, Password);
        var before = await ClientStateSnapshotAsync();
        using var response = await ClientLoginAsync(DoctorEmail, Password, headers);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().ShouldBe("Request.InvalidClientType");
        json.GetProperty("title").GetString().ShouldBe("El tipo de cliente debe ser web o mobile, con un único valor.");
        json.TryGetProperty("accessToken", out _).ShouldBeFalse();
        json.TryGetProperty("refreshToken", out _).ShouldBeFalse();
        json.TryGetProperty("requiresWebFirstAccess", out _).ShouldBeFalse();
        string.Equals(await ClientStateSnapshotAsync(), before, StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Cliente_declarado_mobile_con_clave_incorrecta_no_revela_primer_acceso()
    {
        await PrepareClientDoctorAsync();
        var before = await ClientStateSnapshotAsync();
        using var response = await ClientLoginAsync(DoctorEmail, WrongPassword, [ClientTypeHeader.Mobile]);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().ShouldBe("User.InvalidCredentials");
        json.TryGetProperty("requiresWebFirstAccess", out _).ShouldBeFalse();
        json.TryGetProperty("accessToken", out _).ShouldBeFalse();
        (await CountSessionsAsync()).ShouldBe(0);
        string.Equals(await ClientStateSnapshotAsync(), before, StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Cliente_declarado_mobile_Doctor_con_2FA_no_recibe_aviso_ni_tokens()
    {
        await PrepareClientDoctorAsync();
        await Factory.EnableTwoFactorAsync(DoctorEmail);
        var before = await ClientStateSnapshotAsync();
        using var response = await ClientLoginAsync(DoctorEmail, Password, [ClientTypeHeader.Mobile]);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        AssertClientJson(json, notice: false, step: null);
        json.GetProperty("requiresTwoFactor").GetBoolean().ShouldBeTrue();
        json.GetProperty("accessToken").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("accessTokenExpiresAt").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("refreshToken").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("refreshTokenExpiresAt").ValueKind.ShouldBe(JsonValueKind.Null);
        (await CountSessionsAsync()).ShouldBe(0);
        string.Equals(await ClientStateSnapshotAsync(), before, StringComparison.Ordinal).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cliente_declarado_ignora_body_query_y_UserAgent_para_clasificar(bool withHeader)
    {
        await PrepareClientDoctorAsync();
        using var response = await ClientLoginAsync(DoctorEmail, Password,
            withHeader ? [ClientTypeHeader.Mobile] : null, spoofInput: true);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        AssertClientJson(json, notice: withHeader, step: "change_password");
        var login = json.Deserialize<LoginResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        ReadAccessToken(login).GetClaim(AppClaims.Role).Value.ShouldBe(nameof(UserRole.Doctor));
    }

    [Fact]
    public async Task Cliente_declarado_no_altera_cambio_temporal_refresh_ni_logout()
    {
        await PrepareClientDoctorAsync();
        using var loginResponse = await ClientLoginAsync(DoctorEmail, Password, [ClientTypeHeader.Mobile]);
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = await loginResponse.ReadLoginAsync();
        login.RequiresWebFirstAccess.ShouldBeTrue();
        var sid = ReadAccessToken(login).GetClaim(AppClaims.SessionId).Value;
        using var changeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-temporary-password")
        { Content = JsonContent.Create(new { newPassword = "NuevaClave204" }) };
        changeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        changeRequest.Headers.Add(ClientTypeHeader.Name, "valor-ignorado-fuera-de-login");
        using var changed = await Client.SendAsync(changeRequest);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var changedJson = await changed.Content.ReadFromJsonAsync<JsonElement>();
        AssertClientJson(changedJson, notice: false, step: "doctor_profile");
        var changedTokens = changedJson.Deserialize<LoginResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        ReadAccessToken(changedTokens).GetClaim(AppClaims.SessionId).Value.ShouldBe(sid);
        ReadAccessToken(changedTokens).GetClaim(AppClaims.TokenId).Value.ShouldNotBe(ReadAccessToken(login).GetClaim(AppClaims.TokenId).Value);
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        { Content = JsonContent.Create(new { refreshToken = changedTokens.RefreshToken }) };
        refreshRequest.Headers.Add(ClientTypeHeader.Name, "valor-ignorado-fuera-de-login");
        using var refreshed = await Client.SendAsync(refreshRequest);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refreshJson = await refreshed.Content.ReadFromJsonAsync<JsonElement>();
        refreshJson.TryGetProperty("requiresWebFirstAccess", out _).ShouldBeFalse();
        var refreshTokens = refreshJson.Deserialize<RefreshSessionResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        refreshTokens.PendingStep.ShouldBe("doctor_profile");
        new JsonWebTokenHandler().ReadJsonWebToken(refreshTokens.AccessToken).GetClaim(AppClaims.SessionId).Value.ShouldBe(sid);
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        { Content = JsonContent.Create(new { refreshToken = refreshTokens.RefreshToken }) };
        logoutRequest.Headers.Add(ClientTypeHeader.Name, "valor-ignorado-fuera-de-login");
        using var closed = await Client.SendAsync(logoutRequest);
        closed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var session = (await FindUserAsync(DoctorEmail))!.Sessions.ShouldHaveSingleItem();
        session.Id.ToString(CultureInfo.InvariantCulture).ShouldBe(sid);
        session.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
    }

    private const string ClientTestAgent = "Mobile/PruebasSGCM204";

    private void SetClientClock() =>
        Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private async Task PrepareClientDoctorAsync(bool temporary = true, bool hasProfile = false)
    {
        SetClientClock();
        await SaveUserAsync(UserRole.Doctor, DoctorEmail, Password, temporary);
        if (hasProfile)
        {
            var user = (await FindUserAsync(DoctorEmail))!;
            using var scope = Factory.Services.CreateScope();
            await TestDataSeeder.InsertDoctorProfileAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), user.Id);
        }
    }

    private async Task<HttpResponseMessage> ClientLoginAsync(string email, string password, string[]? headers, bool spoofInput = false)
    {
        if (headers is { Length: 1 } && headers[0].Length == 0)
            return await ClientLoginWithEmptyHeaderAsync(email, password);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login" + (spoofInput ? "?clientType=mobile" : ""));
        request.Headers.UserAgent.ParseAdd(ClientTestAgent);
        if (headers is not null) request.Headers.TryAddWithoutValidation(ClientTypeHeader.Name, headers).ShouldBeTrue();
        request.Content = spoofInput
            ? JsonContent.Create(new { email, password, clientType = ClientTypeHeader.Web, requiresWebFirstAccess = true, role = "Administrator" })
            : JsonContent.Create(new LoginRequest(email, password));
        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> ClientLoginWithEmptyHeaderAsync(string email, string password)
    {
        // El adaptador HttpClient/TestServer omite la cabecera vacía al copiarla. Se entrega
        // explícitamente al mismo pipeline HTTP para distinguirla de una cabecera ausente.
        await using var body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new LoginRequest(email, password),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var context = await Factory.Server.SendAsync(context =>
        {
            context.Request.Method = "POST";
            context.Request.Scheme = "https";
            context.Request.Path = "/api/auth/login";
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = body.Length;
            context.Request.Body = body;
            context.Request.Headers[ClientTypeHeader.Name] = string.Empty;
            context.Request.Headers.ContainsKey(ClientTypeHeader.Name).ShouldBeTrue();
        });
        await using var responseBody = context.Response.Body;
        using var reader = new StreamReader(responseBody);
        return new HttpResponseMessage((HttpStatusCode)context.Response.StatusCode)
        { Content = new StringContent(await reader.ReadToEndAsync(), System.Text.Encoding.UTF8, "application/json") };
    }

    private static void AssertClientJson(JsonElement json, bool notice, string? step)
    {
        string[] oldFields = ["requiresTwoFactor", "accessToken", "accessTokenExpiresAt", "refreshToken", "refreshTokenExpiresAt", "pendingStep"];
        json.EnumerateObject().Select(p => p.Name).Order()
            .ShouldBe((notice ? oldFields.Append("requiresWebFirstAccess") : oldFields).Order());
        json.GetProperty("pendingStep").GetString().ShouldBe(step);
        if (notice) json.GetProperty("requiresWebFirstAccess").GetBoolean().ShouldBeTrue();
    }

    private async Task<string> ClientStateSnapshotAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await context.Database.SqlQuery<string>(
            $"SELECT to_jsonb(u)::text AS \"Value\" FROM app.\"user\" u ORDER BY u.id").ToListAsync();
        var sessions = await context.Database.SqlQuery<string>(
            $"SELECT to_jsonb(s)::text AS \"Value\" FROM app.user_session s ORDER BY s.id").ToListAsync();
        var tokens = await context.Database.SqlQuery<string>(
            $"SELECT to_jsonb(t)::text AS \"Value\" FROM app.user_token t ORDER BY t.id").ToListAsync();
        return string.Join("\n", users.Concat(sessions).Concat(tokens));
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
