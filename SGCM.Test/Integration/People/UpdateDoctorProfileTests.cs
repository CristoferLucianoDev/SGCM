using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Serilog.Core;
using Serilog.Events;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Identity;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class UpdateDoctorProfileTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Url = "/api/doctors/me/profile";
    private const string Email = "doctor255@correo.com";
    private const string Password = "Sintetica255";
    private const string CommitConstraint = "uq_update_doctor_profile_commit_probe";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Swagger_documenta_el_PUT_protegido_y_sus_siete_campos_sin_colisionar_con_el_alta()
    {
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })));
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        paths.GetProperty("/api/doctors/profile").TryGetProperty("post", out _).ShouldBeTrue();
        var put = paths.GetProperty(Url).GetProperty("put");
        put.GetProperty("security").EnumerateArray().Any(scheme => scheme.TryGetProperty("Bearer", out _)).ShouldBeTrue();
        var responses = put.GetProperty("responses");
        foreach (var status in new[] { "204", "400", "401", "403", "404", "500" })
            responses.TryGetProperty(status, out _).ShouldBeTrue();
        responses.GetProperty("204").TryGetProperty("content", out _).ShouldBeFalse();
        put.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema")
            .GetProperty("$ref").GetString().ShouldBe("#/components/schemas/UpdateDoctorProfileRequest");
        var schema = json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("UpdateDoctorProfileRequest");
        var properties = schema.GetProperty("properties").EnumerateObject().Select(value => value.Name);
        properties.ShouldBe(new[] { "phoneNumber", "provinceId", "municipalityId", "addressLine",
            "consultationFee", "yearsOfExperience", "biography" }, ignoreOrder: true);
        var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString());
        required.ShouldContain("consultationFee");
        required.ShouldContain("yearsOfExperience");
    }

    [Fact]
    public async Task Actualiza_solo_el_perfil_propio_e_ignora_campos_ajenos_aunque_la_identidad_sea_legada()
    {
        var catalog = await SeedCatalogAsync();
        var owner = await SeedDoctorAsync(catalog, Email, legacyIdentity: true);
        var other = await SeedDoctorAsync(catalog, "otro255@correo.com");
        owner.Id.ShouldNotBe(other.Id);
        owner.UserId.ShouldNotBe(other.UserId);
        var before = await SnapshotAsync(other.Id);
        var sessions = await SessionSnapshotAsync();
        var requester = (await FindUserAsync(Email))!;
        var otherUser = (await FindUserAsync("otro255@correo.com"))!;
        var body = JsonSerializer.SerializeToNode(Profile(catalog), JsonOptions)!.AsObject();
        body["userId"] = other.UserId;
        body["doctorId"] = other.Id;
        body["firstName"] = "Identidad alterada";
        body["lastName"] = "Apellido alterado";
        body["licenseNumber"] = "LIC-ALTERADA";
        body["specialtyIds"] = new JsonArray(catalog.Second);
        body["isActive"] = false;
        body["role"] = "Administrator";
        body["sessionId"] = otherUser.Sessions.Single().Id;
        requester.Sessions.Single().Id.ShouldNotBe(otherUser.Sessions.Single().Id);

        using var response = await SendAsync(HttpMethod.Put, Url, owner.Login.AccessToken, body);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
        await AssertUpdatedAsync(owner, Profile(catalog), [catalog.First]);
        (await SnapshotAsync(other.Id)).ShouldBe(before);
        (await FindUserAsync(Email))!.Role.ShouldBe(UserRole.Doctor);
        (await FindUserAsync("otro255@correo.com"))!.Role.ShouldBe(UserRole.Doctor);
        string.Equals(await SessionSnapshotAsync(), sessions, StringComparison.Ordinal).ShouldBeTrue();
    }

    [Theory]
    [InlineData("anónimo", HttpStatusCode.Unauthorized, null)]
    [InlineData("paciente", HttpStatusCode.Forbidden, null)]
    [InlineData("administrador", HttpStatusCode.Forbidden, null)]
    [InlineData("pendiente", HttpStatusCode.Forbidden, null)]
    [InlineData("revocada", HttpStatusCode.Unauthorized, "User.SessionInvalid")]
    [InlineData("vencida", HttpStatusCode.Unauthorized, "User.SessionInvalid")]
    [InlineData("ajena", HttpStatusCode.Unauthorized, "User.SessionInvalid")]
    [InlineData("temporal desactualizado", HttpStatusCode.Forbidden, "Doctor.TemporaryPasswordChangeRequired")]
    [InlineData("inactiva", HttpStatusCode.Forbidden, "User.AccountInactive")]
    public async Task Rechaza_credenciales_o_estado_no_autorizado_sin_alterar_el_perfil(
        string scenario, HttpStatusCode status, string? code)
    {
        var catalog = await SeedCatalogAsync();
        var owner = await SeedDoctorAsync(catalog, Email, temporary: scenario is "pendiente" or "temporal desactualizado");
        var token = owner.Login.AccessToken;
        var session = (await FindUserAsync(Email))!.Sessions.Single();
        switch (scenario)
        {
            case "anónimo": token = null; break;
            case "paciente":
            case "administrador":
                await SaveUserAsync(scenario == "paciente" ? UserRole.Patient : UserRole.Administrator,
                    "otro255@correo.com", Password, false);
                token = (await LoginSuccessfullyAsync("otro255@correo.com", Password)).AccessToken;
                break;
            case "revocada":
                using (var logout = await Client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = owner.Login.RefreshToken }))
                    logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                break;
            case "vencida": Clock.UtcNow = session.ExpiresAt; break;
            case "ajena":
                await SeedDoctorAsync(catalog, "otro255@correo.com");
                var foreignSession = (await FindUserAsync("otro255@correo.com"))!.Sessions.Single();
                token = Factory.Services.GetRequiredService<ITokenService>()
                    .CreateAccessToken(owner.UserId, UserRole.Doctor, foreignSession.Id, null).Value;
                break;
            case "temporal desactualizado":
                token = Factory.Services.GetRequiredService<ITokenService>()
                    .CreateAccessToken(owner.UserId, UserRole.Doctor, session.Id, null).Value;
                break;
            case "inactiva":
                await Factory.UpdateUserAsync(Email, user => user.Deactivate().IsSuccess.ShouldBeTrue());
                break;
        }

        if (scenario == "pendiente") owner.Login.PendingStep.ShouldBe("change_password");
        var before = await SnapshotAsync(owner.Id);
        var sessions = await SessionSnapshotAsync();

        using var response = await SendAsync(HttpMethod.Put, Url, token, Profile(catalog));

        await AssertStatusAsync(response, status, code);
        (await SnapshotAsync(owner.Id)).ShouldBe(before);
        string.Equals(await SessionSnapshotAsync(), sessions, StringComparison.Ordinal).ShouldBeTrue();
    }

    [Theory]
    [InlineData("consultationFee", "omitido", null)]
    [InlineData("yearsOfExperience", "omitido", null)]
    [InlineData("consultationFee", "nulo", null)]
    [InlineData("yearsOfExperience", "nulo", null)]
    [InlineData("consultationFee", "tipo", null)]
    [InlineData("yearsOfExperience", "tipo", null)]
    [InlineData("consultationFee", "cadena numérica", null)]
    [InlineData("yearsOfExperience", "cadena numérica", null)]
    [InlineData("consultationFee", "rango", "Doctor.InvalidConsultationFee")]
    [InlineData("yearsOfExperience", "rango", "Doctor.InvalidYearsOfExperience")]
    public async Task Rechaza_numeros_invalidos_sin_modificar_el_perfil(string field, string problem, string? code)
    {
        var catalog = await SeedCatalogAsync();
        var owner = await SeedDoctorAsync(catalog, Email);
        var before = await SnapshotAsync(owner.Id);
        var body = JsonSerializer.SerializeToNode(Profile(catalog), JsonOptions)!.AsObject();
        switch (problem)
        {
            case "omitido": body.Remove(field).ShouldBeTrue(); break;
            case "nulo": body[field] = null; break;
            case "tipo": body[field] = new JsonObject(); break;
            case "cadena numérica": body[field] = "0"; break;
            case "rango": body[field] = -1; break;
        }

        using var response = await SendAsync(HttpMethod.Put, Url, owner.Login.AccessToken, body);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, code);
        (await SnapshotAsync(owner.Id)).ShouldBe(before);
    }

    [Theory]
    [InlineData("provincia", "Doctor.ProvinceNotFound")]
    [InlineData("municipio", "Doctor.MunicipalityNotFound")]
    [InlineData("incompatible", "Doctor.MunicipalityProvinceMismatch")]
    public async Task Rechaza_ubicacion_invalida_sin_modificar_el_perfil(string scenario, string code)
    {
        var catalog = await SeedCatalogAsync();
        var owner = await SeedDoctorAsync(catalog, Email);
        var before = await SnapshotAsync(owner.Id);
        var body = Profile(catalog);
        body = scenario switch
        {
            "provincia" => body with { ProvinceId = int.MaxValue },
            "municipio" => body with { MunicipalityId = int.MaxValue },
            _ => body with { ProvinceId = catalog.Before.ProvinceId }
        };

        using var response = await SendAsync(HttpMethod.Put, Url, owner.Login.AccessToken, body);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, code);
        (await SnapshotAsync(owner.Id)).ShouldBe(before);
    }

    [Fact]
    public async Task Un_JWT_vigente_sin_paso_devuelve_not_found_si_el_perfil_propio_no_existe()
    {
        var catalog = await SeedCatalogAsync();
        var other = await SeedDoctorAsync(catalog, "otro255@correo.com");
        var before = await SnapshotAsync(other.Id);
        await SaveUserAsync(UserRole.Doctor, Email, Password, false);
        var login = await LoginSuccessfullyAsync(Email, Password);
        login.PendingStep.ShouldBe("doctor_profile");
        using (var pending = await SendAsync(HttpMethod.Put, Url, login.AccessToken, Profile(catalog)))
            pending.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var user = (await FindUserAsync(Email))!;
        var access = Factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(user.Id, UserRole.Doctor, user.Sessions.Single().Id, null);

        using var response = await SendAsync(HttpMethod.Put, Url, access.Value, Profile(catalog));

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Doctor.NotFound");
        (await SnapshotAsync(other.Id)).ShouldBe(before);
        await using var scope = Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Doctors.AnyAsync(d => d.UserId == user.Id))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Actualiza_un_perfil_inactivo_y_legado_sin_regularizar_sus_especialidades()
    {
        var catalog = await SeedCatalogAsync();
        var owner = await SeedDoctorAsync(catalog, Email, inactive: true);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM people.doctor_specialty WHERE doctor_id = {owner.Id}");
        }
        var body = Profile(catalog) with { PhoneNumber = " 809-555-0123 ", ConsultationFee = 12.34m, YearsOfExperience = 8 };

        using var response = await SendAsync(HttpMethod.Put, Url, owner.Login.AccessToken, body);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await AssertUpdatedAsync(owner, body with { PhoneNumber = body.PhoneNumber.Trim() }, []);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Un_fallo_despues_del_guardado_revierte_el_perfil_y_devuelve_un_error_seguro(bool failAtCommit)
    {
        var catalog = await SeedCatalogAsync();
        var owner = await SeedDoctorAsync(catalog, Email);
        var before = await SnapshotAsync(owner.Id);
        var sessions = await SessionSnapshotAsync();
        var probe = new FailureProbe(owner.Id, Profile(catalog).PhoneNumber);
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(probe);
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp => new ObservedUnitOfWork(
                ActivatorUtilities.CreateInstance<UnitOfWork>(sp), sp.GetRequiredService<AppDbContext>(),
                failAtCommit ? probe.PrepareCommitFailureAsync : probe.FailAfterSaveAsync, observeAfterSave: true,
                failAtCommit ? probe.ObserveCommit : null));
        }));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/plain");

        using var response = await SendAsync(HttpMethod.Put, Url, owner.Login.AccessToken, Profile(catalog), client);

        await AssertStatusAsync(response, HttpStatusCode.InternalServerError, "Doctor.ProfileUpdateFailed");
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("Ocurrió un error inesperado.");
        body.Contains(owner.Login.AccessToken!, StringComparison.Ordinal).ShouldBeFalse();
        body.Contains(owner.Login.RefreshToken!, StringComparison.Ordinal).ShouldBeFalse();
        body.Contains("division by zero", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        body.Contains(CommitConstraint, StringComparison.Ordinal).ShouldBeFalse();
        body.Contains("Host=", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        problem.RootElement.TryGetProperty("accessToken", out _).ShouldBeFalse();
        problem.RootElement.TryGetProperty("refreshToken", out _).ShouldBeFalse();
        probe.State.ShouldBe(failAtCommit ? PostgresErrorCodes.UniqueViolation : PostgresErrorCodes.DivisionByZero);
        if (failAtCommit) probe.Constraint.ShouldBe(CommitConstraint);
        probe.HasSafeDiagnostic.ShouldBeTrue();
        probe.Contains(owner.Login.AccessToken!).ShouldBeFalse();
        probe.Contains(owner.Login.RefreshToken!).ShouldBeFalse();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ObserveDatabaseAsync(probe.Pid, blocked: false, timeout.Token);
        (await SnapshotAsync(owner.Id)).ShouldBe(before);
        string.Equals(await SessionSnapshotAsync(), sessions, StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Cancelar_despues_del_guardado_revierte_el_perfil_y_libera_la_transaccion()
    {
        var catalog = await SeedCatalogAsync();
        var owner = await SeedDoctorAsync(catalog, Email);
        var before = await SnapshotAsync(owner.Id);
        var sessions = await SessionSnapshotAsync();
        var gate = new SaveGate();
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp => new ObservedUnitOfWork(
                ActivatorUtilities.CreateInstance<UnitOfWork>(sp), sp.GetRequiredService<AppDbContext>(),
                gate.HoldFirstAsync, observeAfterSave: true));
        }));
        using var client = app.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var request = SendAsync(HttpMethod.Put, Url, owner.Login.AccessToken, Profile(catalog), client, requestCancellation.Token);
        try
        {
            var pid = await gate.Arrived.Task.WaitAsync(timeout.Token);
            await requestCancellation.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => request);
            await ObserveDatabaseAsync(pid, blocked: false, timeout.Token);
            (await SnapshotAsync(owner.Id)).ShouldBe(before);
            string.Equals(await SessionSnapshotAsync(), sessions, StringComparison.Ordinal).ShouldBeTrue();
        }
        finally
        {
            await requestCancellation.CancelAsync();
            gate.Release.TrySetResult();
            await DrainAsync(request);
        }
    }

    [Theory]
    [InlineData("actualizar")]
    [InlineData("asociar")]
    [InlineData("desasociar")]
    public async Task El_bloqueo_serializa_reemplazos_y_conserva_los_cambios_de_especialidad(string competitor)
    {
        var catalog = await SeedCatalogAsync();
        var owner = await SeedDoctorAsync(catalog, Email, legacyIdentity: true);
        var specialtiesUrl = $"/api/doctors/me/specialties/{catalog.Second}";
        if (competitor == "desasociar")
        {
            using var preset = await SendAsync(HttpMethod.Post, specialtiesUrl, owner.Login.AccessToken);
            preset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        var before = await SnapshotAsync(owner.Id);
        var firstBody = Profile(catalog);
        var secondBody = new ProfileBody("809-555-0456", catalog.Before.ProvinceId, catalog.Before.MunicipalityId,
            "Otra dirección", 100.49m, 12, "Otro perfil profesional.");
        var gate = new SaveGate(holdSecond: competitor == "actualizar");
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp => new ObservedUnitOfWork(
                ActivatorUtilities.CreateInstance<UnitOfWork>(sp), sp.GetRequiredService<AppDbContext>(), gate.HoldFirstAsync,
                observeAfterSave: false));
        }));
        using var client = app.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        // La desasociación confirma primero para comprobar que el PUT no recrea su vínculo.
        var first = competitor == "desasociar"
            ? SendAsync(HttpMethod.Delete, specialtiesUrl, owner.Login.AccessToken, client: client, cancellationToken: ct)
            : SendAsync(HttpMethod.Put, Url, owner.Login.AccessToken, firstBody, client, ct);
        Task<HttpResponseMessage>? second = null;
        try
        {
            var pid = await gate.Arrived.Task.WaitAsync(ct);
            second = competitor switch
            {
                "actualizar" => SendAsync(HttpMethod.Put, Url, owner.Login.AccessToken, secondBody, client, ct),
                "asociar" => SendAsync(HttpMethod.Post, specialtiesUrl, owner.Login.AccessToken, client: client, cancellationToken: ct),
                _ => SendAsync(HttpMethod.Put, Url, owner.Login.AccessToken, firstBody, client, ct)
            };
            await ObserveDatabaseAsync(pid, blocked: true, ct);
            first.IsCompleted.ShouldBeFalse();
            second.IsCompleted.ShouldBeFalse();
            (await SnapshotAsync(owner.Id)).ShouldBe(before);
            gate.Release.TrySetResult();
            using var firstResponse = await first;
            firstResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            if (competitor == "actualizar")
            {
                await gate.SecondArrived.Task.WaitAsync(ct);
                await AssertUpdatedAsync(owner, firstBody, [catalog.First]);
                gate.ReleaseSecond.TrySetResult();
            }
            using var secondResponse = await second;
            secondResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await AssertUpdatedAsync(owner, competitor == "actualizar" ? secondBody : firstBody,
                competitor == "asociar" ? [catalog.First, catalog.Second] : [catalog.First]);
        }
        finally
        {
            gate.Release.TrySetResult();
            gate.ReleaseSecond.TrySetResult();
            await timeout.CancelAsync();
            await DrainAsync(first);
            await DrainAsync(second);
        }
    }

    private async Task ObserveDatabaseAsync(int pid, bool blocked, CancellationToken cancellationToken)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        FormattableString sql = blocked
            ? (FormattableString)$"""SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE {pid} = ANY(pg_blocking_pids(pid))) AS "Value" """
            : $"""SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND pid <> pg_backend_pid() AND xact_start IS NOT NULL) AS "Value" """;
        while (await db.Database.SqlQuery<bool>(sql).SingleAsync(cancellationToken) != blocked)
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
    }

    private static async Task DrainAsync(Task<HttpResponseMessage>? request)
    {
        if (request is null) return;
        try { (await request).Dispose(); }
        catch (OperationCanceledException) { }
    }

    private async Task<Catalog> SeedCatalogAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locations = await db.Database.SqlQuery<Location>($"""
            SELECT DISTINCT ON (province_id) province_id, id AS municipality_id
            FROM people.municipality ORDER BY province_id, id LIMIT 2
            """).ToListAsync();
        locations.Count.ShouldBe(2);
        var first = Specialty.Create("Medicina interna 255", "Especialidad del médico.").Value;
        var second = Specialty.Create("Pediatría 255", "Especialidad adicional.").Value;
        db.Specialties.AddRange(first, second);
        await db.SaveChangesAsync();
        return new(locations[0], locations[1], first.Id, second.Id);
    }

    private async Task<DoctorAccount> SeedDoctorAsync(Catalog catalog, string email,
        bool legacyIdentity = false, bool temporary = false, bool inactive = false)
    {
        await SaveUserAsync(UserRole.Doctor, email, Password, temporary);
        var user = (await FindUserAsync(email))!;
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = DoctorMother.Create([catalog.First], userId: user.Id, licenseNumber: $"LIC-255-{user.Id}",
            provinceId: catalog.Before.ProvinceId, municipalityId: catalog.Before.MunicipalityId).Value;
        if (inactive) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        if (legacyIdentity)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE people.doctor SET first_name = ' Luis ', last_name = ' Pérez ', license_number = {' ' + doctor.LicenseNumber + ' '}
                WHERE id = {doctor.Id}
                """);

        var login = await LoginSuccessfullyAsync(email, Password);
        return new(user.Id, doctor.Id, login, legacyIdentity ? " Luis " : doctor.FirstName,
            legacyIdentity ? " Pérez " : doctor.LastName,
            legacyIdentity ? $" {doctor.LicenseNumber} " : doctor.LicenseNumber, !inactive);
    }

    private async Task AssertUpdatedAsync(DoctorAccount owner, ProfileBody expected, int[] specialties)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await db.Doctors.AsNoTracking().Include(value => value.Specialties)
            .SingleAsync(value => value.UserId == owner.UserId);
        doctor.Id.ShouldBe(owner.Id);
        doctor.FirstName.ShouldBe(owner.FirstName);
        doctor.LastName.ShouldBe(owner.LastName);
        doctor.LicenseNumber.ShouldBe(owner.License);
        doctor.IsActive.ShouldBe(owner.IsActive);
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(specialties, ignoreOrder: true);
        new ProfileBody(doctor.PhoneNumber.Value, doctor.ProvinceId, doctor.MunicipalityId, doctor.AddressLine,
            doctor.ConsultationFee.Value, doctor.YearsOfExperience, doctor.Biography).ShouldBe(expected);
    }

    private async Task<string[]> SnapshotAsync(int doctorId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return [.. await TestDataSeeder.ReadDoctorsAsync(db, [doctorId]),
            .. await TestDataSeeder.ReadDoctorSpecialtiesAsync(db, [doctorId])];
    }

    private async Task<string> SessionSnapshotAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.SqlQuery<string>($"""
            SELECT COALESCE(jsonb_agg(to_jsonb(s) ORDER BY s.id)::text, '[]') AS "Value" FROM app.user_session s
            """).SingleAsync();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? bearer, object? body = null,
        HttpClient? client = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, url);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await (client ?? Client).SendAsync(request, cancellationToken);
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode status, string? code)
    {
        response.StatusCode.ShouldBe(status);
        if (code is not null) (await response.ReadErrorCodeAsync()).ShouldBe(code);
    }

    private static ProfileBody Profile(Catalog catalog) =>
        new("809-555-0123", catalog.After.ProvinceId, catalog.After.MunicipalityId, "Calle 255", 0m, 0, "Medicina preventiva.");

    private sealed record Location(int ProvinceId, int MunicipalityId);
    private sealed record Catalog(Location Before, Location After, int First, int Second);
    private sealed record DoctorAccount(int UserId, int Id, LoginResponse Login, string FirstName, string LastName,
        string License, bool IsActive);
    private sealed record ProfileBody(string PhoneNumber, int ProvinceId, int MunicipalityId, string AddressLine,
        decimal ConsultationFee, int YearsOfExperience, string Biography);

    private static Task<int> GetPidAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.SqlQuery<int>($"""SELECT pg_backend_pid() AS "Value" """).SingleAsync(ct);

    private sealed class SaveGate(bool holdSecond = false)
    {
        private int _calls;

        public TaskCompletionSource<int> Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HoldFirstAsync(AppDbContext db, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                Arrived.SetResult(await GetPidAsync(db, ct));
                await Release.Task.WaitAsync(ct);
            }
            else if (call == 2 && holdSecond)
            {
                SecondArrived.SetResult();
                await ReleaseSecond.Task.WaitAsync(ct);
            }
        }
    }

    private sealed class FailureProbe(int doctorId, string expectedPhone) : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _logs = new();
        public int Pid { get; private set; }
        public string? State { get; private set; }
        public string? Constraint { get; private set; }
        public bool HasSafeDiagnostic => _logs.Any(entry =>
            entry.MessageTemplate.Text.StartsWith("Fallo al actualizar el perfil médico", StringComparison.Ordinal)
            && entry.Exception is null
            && entry.Properties.TryGetValue("ExceptionType", out var type)
            && type is ScalarValue { Value: nameof(PostgresException) }
            && entry.Properties.TryGetValue("TraceId", out var trace)
            && trace is ScalarValue { Value: string id } && !string.IsNullOrWhiteSpace(id));

        public void Emit(LogEvent logEvent) => _logs.Enqueue(logEvent);

        public bool Contains(string value) => _logs.Any(entry =>
            entry.RenderMessage().Contains(value, StringComparison.Ordinal)
            || entry.Exception?.ToString().Contains(value, StringComparison.Ordinal) == true);

        private async Task VerifyFlushedAsync(AppDbContext db, CancellationToken ct)
        {
            Pid = await GetPidAsync(db, ct);
            var phone = await db.Database.SqlQuery<string>(
                $"SELECT phone_number AS \"Value\" FROM people.doctor WHERE id = {doctorId}").SingleAsync(ct);
            phone.ShouldBe(expectedPhone);
        }

        public async Task FailAfterSaveAsync(AppDbContext db, CancellationToken ct)
        {
            await VerifyFlushedAsync(db, ct);
            try { await db.Database.ExecuteSqlRawAsync("SELECT 1 / 0", ct); }
            catch (PostgresException error) { State = error.SqlState; throw; }
        }

        public async Task PrepareCommitFailureAsync(AppDbContext db, CancellationToken ct)
        {
            await VerifyFlushedAsync(db, ct);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TEMP TABLE update_doctor_profile_commit_probe (
                    id integer,
                    CONSTRAINT uq_update_doctor_profile_commit_probe UNIQUE (id) DEFERRABLE INITIALLY DEFERRED
                ) ON COMMIT DROP;
                INSERT INTO update_doctor_profile_commit_probe (id) VALUES (1), (1)
                """, ct);
        }

        public void ObserveCommit(PostgresException error)
        {
            State = error.SqlState;
            Constraint = error.ConstraintName;
        }
    }

    private sealed class ObservedUnitOfWork(IUnitOfWork inner, AppDbContext db,
        Func<AppDbContext, CancellationToken, Task> observe, bool observeAfterSave,
        Action<PostgresException>? observeCommitError = null) : IUnitOfWork
    {
        public async Task<ITransaction> BeginTransactionAsync(CancellationToken ct)
        {
            var transaction = await inner.BeginTransactionAsync(ct);
            return observeCommitError is null ? transaction : new ObservedCommit(transaction, observeCommitError);
        }

        public async Task SaveChangesAsync(CancellationToken ct)
        {
            if (!observeAfterSave) await observe(db, ct);
            await inner.SaveChangesAsync(ct);
            if (observeAfterSave) await observe(db, ct);
        }
    }

    private sealed class ObservedCommit(ITransaction inner, Action<PostgresException> onError) : ITransaction
    {
        public async Task CommitAsync(CancellationToken ct)
        {
            try { await inner.CommitAsync(ct); }
            catch (PostgresException error) { onError(error); throw; }
        }

        public Task RollbackAsync(CancellationToken ct) => inner.RollbackAsync(ct);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
