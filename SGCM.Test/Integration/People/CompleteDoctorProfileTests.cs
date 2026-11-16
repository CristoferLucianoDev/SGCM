using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using Serilog.Core;
using Serilog.Events;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web;
using SGCM.Web.Contracts.Identity;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class CompleteDoctorProfileTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Url = "/api/doctors/profile";
    private const string Email = "doctor252@correo.com";
    private const string OtherEmail = "otro252@correo.com";
    private const string Password = "Temporal252";
    private const string NewPassword = "Definitiva252";
    private const string License = "LIC-252";
    private const string SignatureFailureDetail = "Fallo de firma controlado.";
    private const string CommitProbeConstraint = "uq_doctor_profile_commit_probe";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Swagger_publica_el_contrato_del_alta_medica_con_autenticacion()
    {
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })));
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths").GetProperty(Url).GetProperty("post");
        operation.GetProperty("security").EnumerateArray().Any(value => value.TryGetProperty("Bearer", out _))
            .ShouldBeTrue();
        var responseCodes = operation.GetProperty("responses").EnumerateObject().Select(value => value.Name);
        foreach (var code in new[] { "200", "400", "401", "403", "404", "409", "500" }) responseCodes.ShouldContain(code);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("CompleteDoctorProfileRequest");
        var required = request.GetProperty("required").EnumerateArray().Select(value => value.GetString());
        required.ShouldContain("consultationFee");
        required.ShouldContain("yearsOfExperience");
        var properties = request.GetProperty("properties");
        foreach (var identity in new[] { "userId", "doctorId", "sessionId", "deviceName", "ipAddress" })
            properties.TryGetProperty(identity, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Completa_el_primer_acceso_y_entrega_tokens_del_perfil_confirmado()
    {
        var catalog = await SeedCatalogAsync();
        var firstAccess = await LoginDoctorAsync(Email, mustChangePassword: true);
        firstAccess.PendingStep.ShouldBe("change_password");
        using var changed = await PostAsync("/api/auth/change-temporary-password", firstAccess.AccessToken,
            new { newPassword = NewPassword });
        await changed.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var pending = await changed.ReadLoginAsync();
        pending.PendingStep.ShouldBe("doctor_profile");
        Claim(pending, AppClaims.SessionId).ShouldBe(Claim(firstAccess, AppClaims.SessionId));
        var other = await LoginDoctorAsync(OtherEmail);
        var payload = JsonSerializer.SerializeToNode(Profile(catalog), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        payload["userId"] = int.Parse(Claim(other, AppClaims.UserId));
        payload["doctorId"] = 999;
        payload["sessionId"] = int.Parse(Claim(other, AppClaims.SessionId));

        using var response = await PostAsync(Url, pending.AccessToken, payload);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var completed = await response.ReadLoginAsync();
        ReadJwt(completed).TryGetClaim(AppClaims.PendingStep, out _).ShouldBeFalse();
        completed.PendingStep.ShouldBeNull();
        completed.RequiresTwoFactor.ShouldBeFalse();
        Claim(completed, AppClaims.UserId).ShouldBe(Claim(pending, AppClaims.UserId));
        Claim(completed, AppClaims.Role).ShouldBe(nameof(UserRole.Doctor));
        Claim(completed, AppClaims.SessionId).ShouldNotBe(Claim(pending, AppClaims.SessionId));
        completed.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        completed.RefreshToken.ShouldNotBe(pending.RefreshToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] responseFields = ["requiresTwoFactor", "accessToken", "accessTokenExpiresAt",
            "refreshToken", "refreshTokenExpiresAt", "pendingStep"];
        json.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(responseFields, ignoreOrder: true);

        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await context.Doctors.Include(d => d.Specialties).SingleAsync();
        doctor.UserId.ShouldBe(int.Parse(Claim(pending, AppClaims.UserId)));
        doctor.FirstName.ShouldBe("Ana");
        doctor.LastName.ShouldBe("Gómez");
        doctor.LicenseNumber.ShouldBe(License);
        doctor.ConsultationFee.Value.ShouldBe(0m);
        doctor.YearsOfExperience.ShouldBe(0);
        doctor.PhoneNumber.Value.ShouldBe("809-555-0100");
        doctor.ProvinceId.ShouldBe(catalog.ProvinceId);
        doctor.MunicipalityId.ShouldBe(catalog.MunicipalityId);
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(new[] { catalog.Active, catalog.Second }, ignoreOrder: true);
        var owner = (await FindUserAsync(Email))!;
        owner.Sessions.Count.ShouldBe(2);
        var newSession = owner.Sessions.Single(session => session.Id == int.Parse(Claim(completed, AppClaims.SessionId)));
        newSession.DeviceName.ShouldBe(UserAgent);
        var refreshHash = scope.ServiceProvider.GetRequiredService<ISecureTokenGenerator>().Hash(completed.RefreshToken!);
        string.Equals(newSession.RefreshTokenHash, refreshHash, StringComparison.Ordinal).ShouldBeTrue();
        (await FindUserAsync(OtherEmail))!.Sessions.Count.ShouldBe(1);

        using var old = await PostAsync($"/api/doctors/me/specialties/{catalog.Active}", pending.AccessToken);
        await old.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        Claim(pending, AppClaims.PendingStep).ShouldBe("doctor_profile");
        using var passwordToken = await PostAsync(Url, firstAccess.AccessToken, Profile(catalog));
        await passwordToken.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        foreach (var tokens in new[] { completed, pending })
        {
            using var retry = await PostAsync(Url, tokens.AccessToken, Profile(catalog));
            await AssertErrorAsync(retry, HttpStatusCode.Conflict, "Doctor.ProfileAlreadyExists");
        }
        using var allowed = await PostAsync($"/api/doctors/me/specialties/{catalog.Active}", completed.AccessToken);
        await AssertErrorAsync(allowed, HttpStatusCode.Conflict, "Doctor.SpecialtyAlreadyAssociated");

        foreach (var tokens in new[] { completed, pending })
        {
            using var refresh = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
            await refresh.ShouldHaveStatusAsync(HttpStatusCode.OK);
            var refreshed = (await refresh.Content.ReadFromJsonAsync<RefreshSessionResponse>())!;
            refreshed.PendingStep.ShouldBeNull();
            new JsonWebToken(refreshed.AccessToken).TryGetClaim(AppClaims.PendingStep, out _).ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData("anónimo", HttpStatusCode.Unauthorized)]
    [InlineData("paciente", HttpStatusCode.Forbidden)]
    [InlineData("administrador", HttpStatusCode.Forbidden)]
    [InlineData("temporal", HttpStatusCode.Forbidden)]
    public async Task La_autorizacion_HTTP_no_depende_del_tipo_de_cliente(string scenario, HttpStatusCode status)
    {
        var catalog = await SeedCatalogAsync();
        string? token = null;
        if (scenario != "anónimo")
        {
            var role = scenario switch
            {
                "paciente" => UserRole.Patient,
                "administrador" => UserRole.Administrator,
                _ => UserRole.Doctor
            };
            await SaveUserAsync(role, Email, Password, mustChangePassword: scenario == "temporal");
            token = (await LoginSuccessfullyAsync(Email, Password)).AccessToken;
        }
        Client.DefaultRequestHeaders.Add("X-SGCM-Client", "web");

        using var response = await PostAsync(Url, token, Profile(catalog));

        await response.ShouldHaveStatusAsync(status);
        await AssertCountsAsync(0, scenario == "anónimo" ? 0 : 1);
    }

    [Theory]
    [InlineData("ajena", HttpStatusCode.Unauthorized, "User.SessionInvalid")]
    [InlineData("revocada", HttpStatusCode.Unauthorized, "User.SessionInvalid")]
    [InlineData("expirada", HttpStatusCode.Unauthorized, "User.SessionInvalid")]
    [InlineData("inactiva", HttpStatusCode.Forbidden, "User.AccountInactive")]
    [InlineData("sin claim pendiente", HttpStatusCode.Forbidden, "Doctor.TemporaryPasswordChangeRequired")]
    public async Task Valida_la_sesion_y_el_estado_actual_aunque_el_JWT_sea_valido(string scenario, HttpStatusCode status, string code)
    {
        var catalog = await SeedCatalogAsync();
        var login = await LoginDoctorAsync(Email, mustChangePassword: scenario == "sin claim pendiente");
        var userId = int.Parse(Claim(login, AppClaims.UserId));
        var sessionId = int.Parse(Claim(login, AppClaims.SessionId));
        var sessions = 1;
        if (scenario == "ajena")
        {
            var other = await LoginDoctorAsync(OtherEmail);
            sessionId = int.Parse(Claim(other, AppClaims.SessionId));
            sessions++;
        }
        if (scenario == "revocada")
        {
            using var logout = await Client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = login.RefreshToken });
            await logout.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }
        if (scenario == "expirada")
            Clock.UtcNow = (await FindUserAsync(Email))!.Sessions.Single(session => session.Id == sessionId).ExpiresAt;
        if (scenario == "inactiva")
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE app.\"user\" SET is_active = false WHERE id = {userId}");
        }
        var token = scenario is "ajena" or "sin claim pendiente"
            ? Factory.Services.GetRequiredService<ITokenService>()
                .CreateAccessToken(userId, UserRole.Doctor, sessionId, pendingStep: null).Value
            : login.AccessToken;

        using var response = await PostAsync(Url, token, Profile(catalog));

        await AssertErrorAsync(response, status, code);
        if (scenario == "revocada")
        {
            using var refresh = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.RefreshToken });
            await refresh.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        }
        await AssertCountsAsync(0, sessions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JwtBearer_rechaza_un_JWT_vencido_o_con_firma_alterada(bool expired)
    {
        var catalog = await SeedCatalogAsync();
        var login = await LoginDoctorAsync(Email);
        string token;
        if (expired)
        {
            var now = Clock.UtcNow;
            Clock.UtcNow = now.AddDays(-1);
            try
            {
                token = Factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(
                    int.Parse(Claim(login, AppClaims.UserId)), UserRole.Doctor, int.Parse(Claim(login, AppClaims.SessionId)),
                    PendingStep.DoctorProfile).Value;
            }
            finally
            {
                Clock.UtcNow = now;
            }
        }
        else
        {
            var segments = login.AccessToken!.Split('.');
            segments[2] = (segments[2][0] == 'A' ? "B" : "A") + segments[2][1..];
            token = string.Join('.', segments);
        }

        using var response = await PostAsync(Url, token, Profile(catalog));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Any(value => value.Parameter?.Contains("invalid_token") == true).ShouldBeTrue();
        await AssertCountsAsync(0, 1);
    }

    [Theory]
    [InlineData("consultationFee")]
    [InlineData("yearsOfExperience")]
    public async Task Rechaza_omitir_un_numero_obligatorio_en_JSON(string field)
    {
        var catalog = await SeedCatalogAsync();
        var login = await LoginDoctorAsync(Email);
        var payload = JsonSerializer.SerializeToNode(Profile(catalog), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        payload.Remove(field).ShouldBeTrue();

        using var response = await PostAsync(Url, login.AccessToken, payload);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        await AssertCountsAsync(0, 1);
    }

    [Theory]
    [InlineData("provincia", HttpStatusCode.BadRequest, "Doctor.ProvinceNotFound")]
    [InlineData("municipio", HttpStatusCode.BadRequest, "Doctor.MunicipalityNotFound")]
    [InlineData("incompatible", HttpStatusCode.BadRequest, "Doctor.MunicipalityProvinceMismatch")]
    [InlineData("especialidad", HttpStatusCode.NotFound, "Specialty.NotFound")]
    [InlineData("inactiva", HttpStatusCode.Conflict, "Specialty.Inactive")]
    [InlineData("repetida", HttpStatusCode.BadRequest, "Doctor.SpecialtyAlreadyAssociated")]
    [InlineData("tarifa", HttpStatusCode.BadRequest, "Doctor.InvalidConsultationFee")]
    public async Task Informa_el_error_especifico_sin_crear_perfil_ni_sesion(string scenario, HttpStatusCode status, string code)
    {
        var catalog = await SeedCatalogAsync();
        var login = await LoginDoctorAsync(Email);
        var valid = Profile(catalog);
        var invalid = scenario switch
        {
            "provincia" => valid with { ProvinceId = int.MaxValue },
            "municipio" => valid with { MunicipalityId = int.MaxValue },
            "incompatible" => valid with { ProvinceId = catalog.OtherProvinceId },
            "especialidad" => valid with { SpecialtyIds = [catalog.Active, int.MaxValue] },
            "inactiva" => valid with { SpecialtyIds = [catalog.Active, catalog.Inactive] },
            "repetida" => valid with { SpecialtyIds = [catalog.Active, catalog.Active] },
            "tarifa" => valid with { ConsultationFee = 0.001m },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        using var response = await PostAsync(Url, login.AccessToken, invalid);

        await AssertErrorAsync(response, status, code);
        await AssertCountsAsync(0, 1);
    }

    [Fact]
    public async Task Un_perfil_legado_sin_vinculos_impide_otra_alta()
    {
        var catalog = await SeedCatalogAsync();
        var login = await LoginDoctorAsync(Email);
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestDataSeeder.InsertDoctorProfileAsync(context, int.Parse(Claim(login, AppClaims.UserId)));

        using var response = await PostAsync(Url, login.AccessToken, Profile(catalog));

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Doctor.ProfileAlreadyExists");
        await AssertCountsAsync(1, 1, links: 0);
    }

    [Fact]
    public async Task Comprueba_licencia_duplicada_usando_el_valor_normalizado()
    {
        var catalog = await SeedCatalogAsync();
        var first = await LoginDoctorAsync(Email);
        var second = await LoginDoctorAsync(OtherEmail);
        using var completed = await PostAsync(Url, first.AccessToken, Profile(catalog) with { LicenseNumber = License });
        await completed.ShouldHaveStatusAsync(HttpStatusCode.OK);

        using var response = await PostAsync(Url, second.AccessToken, Profile(catalog));

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Doctor.LicenseNumberAlreadyInUse");
        await AssertCountsAsync(1, 3, links: 2);
    }

    [Theory]
    [InlineData(true, "Doctor.ProfileAlreadyExists")]
    [InlineData(false, "Doctor.LicenseNumberAlreadyInUse")]
    public async Task Dos_altas_concurrentes_confirman_solo_un_perfil_y_una_sesion_nueva(bool sameUser, string error)
    {
        var catalog = await SeedCatalogAsync();
        var claimant = await LoginDoctorAsync(Email);
        var contender = sameUser ? claimant : await LoginDoctorAsync(OtherEmail);
        var gate = new SaveGate(sameUser ? 1 : 2);
        using var app = WithSaveGate(gate);
        using var client = app.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var first = PostAsync(Url, claimant.AccessToken, Profile(catalog), client, ct);
        Task<HttpResponseMessage>? second = null;
        try
        {
            var firstPid = await gate.WaitAsync(0, ct);
            second = PostAsync(Url, contender.AccessToken, Profile(catalog), client, ct);
            if (sameUser)
            {
                await WaitForBlockedRequestAsync(firstPid, ct);
            }
            else
            {
                // Ambos handlers llegaron a guardar después de consultar la licencia como disponible.
                var secondPid = await gate.WaitAsync(1, ct);
                secondPid.ShouldNotBe(firstPid);
            }

            gate.Release(0);
            using var winner = await first;
            await winner.ShouldHaveStatusAsync(HttpStatusCode.OK);
            (await winner.ReadLoginAsync()).PendingStep.ShouldBeNull();
            if (!sameUser) gate.Release(1);
            using var loser = await second;
            await AssertErrorAsync(loser, HttpStatusCode.Conflict, error);
            await AssertCountsAsync(1, sameUser ? 2 : 3, links: 2);
            await using var scope = Factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await context.Doctors.SingleAsync(ct)).UserId.ShouldBe(int.Parse(Claim(claimant, AppClaims.UserId)));
        }
        finally
        {
            gate.ReleaseAll();
            await timeout.CancelAsync();
            await DrainAsync(first);
            await DrainAsync(second);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task La_desactivacion_y_el_alta_respetan_el_orden_de_bloqueos_de_PostgreSQL(bool altaPrimero)
    {
        var catalog = await SeedCatalogAsync();
        var login = await LoginDoctorAsync(Email);
        var gate = new SaveGate(1);
        using var app = WithSaveGate(gate);
        using var client = app.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        Task<HttpResponseMessage>? completion = null;
        Task<int>? deactivation = null;
        try
        {
            if (altaPrimero)
            {
                completion = PostAsync(Url, login.AccessToken, Profile(catalog), client, ct);
                var blockerPid = await gate.WaitAsync(0, ct);
                deactivation = DeactivateAsync();
                await WaitForBlockedRequestAsync(blockerPid, ct);
                gate.Release(0);
                using var response = await completion;
                await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
                (await deactivation).ShouldBe(1);
                await transaction.CommitAsync(ct);
                await AssertCountsAsync(1, 2, links: 2);
            }
            else
            {
                (await DeactivateAsync()).ShouldBe(1);
                var blockerPid = await BackendPidAsync(context, ct);
                completion = PostAsync(Url, login.AccessToken, Profile(catalog), client, ct);
                await WaitForBlockedRequestAsync(blockerPid, ct);
                await transaction.CommitAsync(ct);
                using var response = await completion;
                await AssertErrorAsync(response, HttpStatusCode.Conflict, "Specialty.Inactive");
                await AssertCountsAsync(0, 1);
            }

            (await context.Specialties.AsNoTracking().SingleAsync(s => s.Id == catalog.Active, ct)).IsActive.ShouldBeFalse();
        }
        finally
        {
            gate.ReleaseAll();
            await timeout.CancelAsync();
            await DrainAsync(completion);
            if (deactivation is not null)
            {
                try { await deactivation; }
                catch (OperationCanceledException) { }
            }
        }

        Task<int> DeactivateAsync() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.specialty SET is_active = false WHERE id = {catalog.Active}", ct);
    }

    [Fact]
    public async Task El_municipio_esta_protegido_antes_de_insertar_el_perfil()
    {
        var catalog = await SeedCatalogAsync();
        var login = await LoginDoctorAsync(Email);
        var gate = new SaveGate(1);
        using var app = WithSaveGate(gate);
        using var client = app.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var request = PostAsync(Url, login.AccessToken, Profile(catalog), client, ct);
        Task<int>? exclusiveLock = null;
        try
        {
            var blockerPid = await gate.WaitAsync(0, ct);
            exclusiveLock = context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM people.municipality WHERE id = {catalog.MunicipalityId} FOR UPDATE", ct);
            await WaitForBlockedRequestAsync(blockerPid, ct);
            gate.Release(0);
            using var response = await request;
            await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
            await exclusiveLock;
            await transaction.RollbackAsync(ct);
            await AssertCountsAsync(1, 2, links: 2);
        }
        finally
        {
            gate.ReleaseAll();
            await timeout.CancelAsync();
            await DrainAsync(request);
            if (exclusiveLock is not null)
            {
                try { await exclusiveLock; }
                catch (OperationCanceledException) { }
            }
        }
    }

    [Theory]
    [InlineData("guardado")]
    [InlineData("firma")]
    [InlineData("commit")]
    public async Task Un_fallo_revierte_perfil_vinculos_y_sesion_sin_entregar_credenciales(string stage)
    {
        var catalog = await SeedCatalogAsync();
        var login = await LoginDoctorAsync(Email);
        var probe = new FailureProbe();
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(probe);
            services.RemoveAll<ITokenService>();
            services.AddScoped<ITokenService>(sp =>
                new ObservedTokenService(ActivatorUtilities.CreateInstance<JwtTokenService>(sp), probe, stage == "firma"));
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp => new FailingUnitOfWork(
                ActivatorUtilities.CreateInstance<UnitOfWork>(sp), sp.GetRequiredService<AppDbContext>(), probe, stage));
        }));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/plain");

        using var response = await PostAsync(Url, login.AccessToken, Profile(catalog), client);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Contains(login.AccessToken!, StringComparison.Ordinal)
            .ShouldBeFalse("El error HTTP debe omitir la credencial de la solicitud.");
        body.Contains(SignatureFailureDetail, StringComparison.Ordinal)
            .ShouldBeFalse("El error HTTP debe omitir detalles internos.");
        body.Contains("division by zero", StringComparison.OrdinalIgnoreCase)
            .ShouldBeFalse("El error HTTP debe omitir detalles internos de PostgreSQL.");
        body.Contains(CommitProbeConstraint, StringComparison.Ordinal).ShouldBeFalse();
        body.ShouldNotContain("\"accessToken\"");
        body.ShouldNotContain("\"refreshToken\"");
        await AssertErrorAsync(response, HttpStatusCode.InternalServerError, "Doctor.ProfileCompletionFailed");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        probe.Signatures.ShouldBe(stage == "guardado" ? 0 : 1);
        if (stage == "guardado") probe.DatabaseError!.SqlState.ShouldBe(PostgresErrorCodes.DivisionByZero);
        if (stage == "commit")
        {
            probe.DatabaseError!.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            probe.DatabaseError.ConstraintName.ShouldBe(CommitProbeConstraint);
            body.Contains(probe.Token!.Value, StringComparison.Ordinal).ShouldBeFalse();
            probe.ContainsInLogs(probe.Token.Value).ShouldBeFalse();
        }
        probe.ContainsInLogs("Fallo al completar el perfil médico").ShouldBeTrue();
        probe.ContainsInLogs(login.AccessToken!).ShouldBeFalse();
        probe.ContainsInLogs(login.RefreshToken!).ShouldBeFalse();
        probe.ContainsInLogs(SignatureFailureDetail).ShouldBeFalse();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await WaitForTransactionEndAsync(probe.BackendPid!.Value, timeout.Token);
        await AssertCountsAsync(0, 1);
        using var refresh = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.RefreshToken });
        await refresh.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await refresh.Content.ReadFromJsonAsync<RefreshSessionResponse>())!.PendingStep.ShouldBe("doctor_profile");
    }

    [Fact]
    public async Task Cancelar_despues_de_guardar_libera_la_transaccion_y_revierte_el_alta()
    {
        var catalog = await SeedCatalogAsync();
        var login = await LoginDoctorAsync(Email);
        var gate = new SaveGate(2);
        using var app = WithSaveGate(gate);
        using var client = app.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var request = PostAsync(Url, login.AccessToken, Profile(catalog), client, requestCancellation.Token);
        try
        {
            var pid = await gate.WaitAsync(0, timeout.Token);
            gate.Release(0);
            // El primer guardado ya envió perfil y sesión a PostgreSQL, y la transacción sigue abierta.
            (await gate.WaitAsync(1, timeout.Token)).ShouldBe(pid);
            await requestCancellation.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => request);
            await WaitForTransactionEndAsync(pid, timeout.Token);
            await AssertCountsAsync(0, 1);
        }
        finally
        {
            await requestCancellation.CancelAsync();
            gate.ReleaseAll();
            await DrainAsync(request);
        }
    }

    private WebApplicationFactory<Program> WithSaveGate(SaveGate gate) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp => new GatedUnitOfWork(
                ActivatorUtilities.CreateInstance<UnitOfWork>(sp), sp.GetRequiredService<AppDbContext>(), gate));
        }));

    private async Task WaitForBlockedRequestAsync(int blockingPid, CancellationToken cancellationToken)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        while (!await context.Database.SqlQuery<bool>(
            $"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE {blockingPid} = ANY(pg_blocking_pids(pid))) AS \"Value\"")
            .SingleAsync(cancellationToken))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private async Task WaitForTransactionEndAsync(int pid, CancellationToken cancellationToken)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        while (await context.Database.SqlQuery<bool>(
            $"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND pid <> pg_backend_pid() AND xact_start IS NOT NULL) AS \"Value\"")
            .SingleAsync(cancellationToken))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private static Task<int> BackendPidAsync(AppDbContext context, CancellationToken cancellationToken) =>
        context.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync(cancellationToken);

    private static async Task DrainAsync(Task<HttpResponseMessage>? request)
    {
        if (request is null) return;
        try { (await request).Dispose(); }
        catch (OperationCanceledException) { }
    }

    private async Task AssertCountsAsync(int profiles, int sessions, int links = 0)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Doctors.CountAsync()).ShouldBe(profiles);
        (await context.Set<UserSession>().CountAsync()).ShouldBe(sessions);
        (await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM people.doctor_specialty")
            .SingleAsync()).ShouldBe(links);
    }

    private async Task<LoginResponse> LoginDoctorAsync(string email, bool mustChangePassword = false)
    {
        await SaveUserAsync(UserRole.Doctor, email, Password, mustChangePassword);
        var login = await LoginSuccessfullyAsync(email, Password);
        login.PendingStep.ShouldBe(mustChangePassword ? "change_password" : "doctor_profile");
        return login;
    }

    private async Task<Catalog> SeedCatalogAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var active = Specialty.Create("Medicina interna 252", "Especialidad de prueba.").Value;
        var second = Specialty.Create("Pediatría 252", "Especialidad de prueba.").Value;
        var inactive = Specialty.Create("Especialidad inactiva 252", "Especialidad de prueba.").Value;
        inactive.Deactivate().IsSuccess.ShouldBeTrue();
        context.Specialties.AddRange(active, second, inactive);
        await context.SaveChangesAsync();
        var location = await context.Database.SqlQueryRaw<Location>(
                "SELECT id AS municipality_id, province_id FROM people.municipality ORDER BY id LIMIT 1")
            .SingleAsync();
        var otherProvince = await context.Database.SqlQuery<int>(
            $"SELECT id AS \"Value\" FROM people.province WHERE id <> {location.ProvinceId} ORDER BY id LIMIT 1").SingleAsync();
        return new(location.ProvinceId, location.MunicipalityId, otherProvince, active.Id, second.Id, inactive.Id);
    }

    private static ProfileBody Profile(Catalog catalog) =>
        new(" Ana ", "Gómez", $" {License} ", " 809-555-0100 ", catalog.ProvinceId, catalog.MunicipalityId,
            "Calle 252", 0m, 0, "Atención médica.", [catalog.Active, catalog.Second]);

    private async Task<HttpResponseMessage> PostAsync(string url, string? token, object? body = null,
        HttpClient? client = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await (client ?? Client).SendAsync(request, cancellationToken);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string error)
    {
        response.StatusCode.ShouldBe(status);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe(error);
        problem.RootElement.TryGetProperty("accessToken", out _).ShouldBeFalse();
        problem.RootElement.TryGetProperty("refreshToken", out _).ShouldBeFalse();
    }

    private static JsonWebToken ReadJwt(LoginResponse tokens) => new(tokens.AccessToken!);

    private static string Claim(LoginResponse tokens, string claim) => ReadJwt(tokens).GetClaim(claim).Value;

    private sealed record Location(int MunicipalityId, int ProvinceId);
    private sealed record Catalog(int ProvinceId, int MunicipalityId, int OtherProvinceId, int Active, int Second, int Inactive);
    private sealed record ProfileBody(string FirstName, string LastName, string LicenseNumber, string PhoneNumber,
        int ProvinceId, int MunicipalityId, string AddressLine, decimal ConsultationFee, int YearsOfExperience,
        string Biography, int[] SpecialtyIds);

    private sealed class SaveGate(int count)
    {
        private readonly TaskCompletionSource<int>[] _arrivals = Enumerable.Range(0, count)
            .Select(_ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        private readonly TaskCompletionSource[] _releases = Enumerable.Range(0, count)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        private int _calls;

        public Task<int> WaitAsync(int index, CancellationToken token) => _arrivals[index].Task.WaitAsync(token);

        public void Release(int index) => _releases[index].TrySetResult();

        public void ReleaseAll()
        {
            foreach (var release in _releases) release.TrySetResult();
        }

        public async Task BeforeSaveAsync(AppDbContext context, CancellationToken token)
        {
            var index = Interlocked.Increment(ref _calls) - 1;
            if (index >= _arrivals.Length) return;
            _arrivals[index].TrySetResult(await BackendPidAsync(context, token));
            await _releases[index].Task.WaitAsync(token);
        }
    }

    private sealed class GatedUnitOfWork(IUnitOfWork inner, AppDbContext context, SaveGate gate) : IUnitOfWork
    {
        public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            inner.BeginTransactionAsync(cancellationToken);

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await gate.BeforeSaveAsync(context, cancellationToken);
            await inner.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class FailureProbe : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _logs = new();

        public int Signatures { get; set; }
        public AccessToken? Token { get; set; }
        public PostgresException? DatabaseError { get; set; }
        public int? BackendPid { get; set; }

        public void Emit(LogEvent logEvent) => _logs.Enqueue(logEvent);

        public bool ContainsInLogs(string value) => _logs.Any(entry =>
            entry.RenderMessage().Contains(value, StringComparison.Ordinal)
            || entry.Exception?.ToString().Contains(value, StringComparison.Ordinal) == true);

        public async Task FailInPostgresAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            try { await context.Database.ExecuteSqlRawAsync("SELECT 1 / 0", cancellationToken); }
            catch (PostgresException error) { DatabaseError = error; throw; }
        }
    }

    private sealed class ObservedTokenService(ITokenService inner, FailureProbe probe, bool fail) : ITokenService
    {
        public AccessToken CreateAccessToken(int userId, UserRole role, int sessionId, PendingStep? pendingStep)
        {
            probe.Signatures++;
            if (fail) throw new InvalidOperationException(SignatureFailureDetail);
            var accessToken = inner.CreateAccessToken(userId, role, sessionId, pendingStep);
            probe.Token = accessToken;
            return accessToken;
        }
    }

    private sealed class FailingUnitOfWork(IUnitOfWork inner, AppDbContext context, FailureProbe probe, string stage) : IUnitOfWork
    {
        public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        {
            var transaction = await inner.BeginTransactionAsync(cancellationToken);
            return stage == "commit" ? new FailingCommit(transaction, context, probe) : transaction;
        }

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await inner.SaveChangesAsync(cancellationToken);
            probe.BackendPid ??= await BackendPidAsync(context, cancellationToken);
            if (stage == "guardado") await probe.FailInPostgresAsync(context, cancellationToken);
        }
    }

    private sealed class FailingCommit(ITransaction inner, AppDbContext context, FailureProbe probe) : ITransaction
    {
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            // La restricción se comprueba en COMMIT. La tabla temporal pertenece a la transacción de prueba.
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TEMP TABLE doctor_profile_commit_probe (
                    id integer,
                    CONSTRAINT uq_doctor_profile_commit_probe UNIQUE (id) DEFERRABLE INITIALLY DEFERRED
                ) ON COMMIT DROP;
                INSERT INTO doctor_profile_commit_probe (id) VALUES (1), (1)
                """, cancellationToken);
            try { await inner.CommitAsync(cancellationToken); }
            catch (PostgresException error) { probe.DatabaseError = error; throw; }
        }

        public Task RollbackAsync(CancellationToken cancellationToken) => inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
