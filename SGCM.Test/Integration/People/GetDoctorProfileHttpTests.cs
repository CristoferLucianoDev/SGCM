using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Serilog.Core;
using Serilog.Events;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class GetDoctorProfileHttpTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Password = "Sintetica259";
    private static readonly string[] PublicFields =
    [
        "id", "fullName", "specialties", "province", "municipality", "addressLine",
        "consultationFee", "yearsOfExperience", "biography"
    ];
    private static readonly string[] AdminFields = ["profile", "phoneNumber", "licenseNumber"];

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public override async Task DisposeAsync()
    {
        try { await Database.ResetAsync(); }
        finally { await base.DisposeAsync(); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Doctor)]
    public async Task El_GET_publico_devuelve_el_contrato_completo_sin_datos_privados(UserRole? role)
    {
        var catalog = await SeedCatalogAsync();
        var doctor = await SeedDoctorAsync(catalog.FirstLocation, [catalog.Pediatric.Id, catalog.Cardio.Id]);
        var token = role switch
        {
            UserRole.Doctor => (await LoginSuccessfullyAsync(doctor.Email, Password)).AccessToken,
            UserRole.Patient => (await LoginAsAsync(UserRole.Patient)).Token,
            _ => null
        };
        using var response = await GetAsync(doctor.Profile.Id, token);

        var json = await ReadPublicAsync(response, doctor.Profile.Id);
        json.GetProperty("fullName").GetString().ShouldBe("Ana  María Núñez");
        AssertLocation(json, catalog.FirstLocation);
        json.GetProperty("addressLine").GetString().ShouldBe(doctor.Profile.AddressLine);
        json.GetProperty("consultationFee").GetDecimal().ShouldBe(doctor.Profile.ConsultationFee.Value);
        json.GetProperty("yearsOfExperience").GetInt32().ShouldBe(doctor.Profile.YearsOfExperience);
        json.GetProperty("biography").GetString().ShouldBe(doctor.Profile.Biography);
        json.GetProperty("specialties").EnumerateArray().Select(item =>
            (item.GetProperty("id").GetInt32(), item.GetProperty("name").GetString().ShouldNotBeNull()))
            .ShouldBe(new[] { (catalog.Cardio.Id, catalog.Cardio.Name), (catalog.Pediatric.Id, catalog.Pediatric.Name) });
    }

    [Fact]
    public async Task El_GET_administrativo_respeta_DoctorId_y_no_reutiliza_la_respuesta_para_un_anonimo()
    {
        var catalog = await SeedCatalogAsync();
        var first = await SeedDoctorAsync(catalog.FirstLocation, [catalog.Cardio.Id]);
        var second = await SeedDoctorAsync(catalog.SecondLocation, [catalog.Pediatric.Id], phone: "809-555-0259");
        var administrator = await LoginAsAsync(UserRole.Administrator);
        using var anonymous = Factory.CreateClient();

        foreach (var doctor in new[] { first, second })
        {
            using var privateResponse = await GetAsync(doctor.Profile.Id, administrator.Token);
            await privateResponse.ShouldHaveStatusAsync(HttpStatusCode.OK);
            AssertNeverStored(privateResponse);
            var administrative = await ReadJsonAsync(privateResponse);
            AssertFields(administrative, AdminFields);
            administrative.GetProperty("phoneNumber").GetString().ShouldBe(doctor.Profile.PhoneNumber.Value);
            administrative.GetProperty("licenseNumber").GetString().ShouldBe(doctor.Profile.LicenseNumber);

            using var publicResponse = await anonymous.GetAsync(ProfileUrl(doctor.Profile.Id));
            var profile = await ReadPublicAsync(publicResponse, doctor.Profile.Id);
            administrative.GetProperty("profile").GetRawText().ShouldBe(profile.GetRawText());
        }

        using var untrusted = new HttpRequestMessage(HttpMethod.Get, ProfileUrl(first.Profile.Id)
            + $"?doctorId={second.Profile.Id}&isAdmin=true&includePrivateData=true&role=Administrator"
            + $"&userId={administrator.UserId}&sessionId={administrator.SessionId}");
        untrusted.Headers.Add("X-Role", nameof(UserRole.Administrator));
        using var untrustedResponse = await anonymous.SendAsync(untrusted);
        await ReadPublicAsync(untrustedResponse, first.Profile.Id);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Doctors.Where(doctor => doctor.Id == first.Profile.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(doctor => doctor.IsActive, false));
        using var errorResponse = await anonymous.GetAsync(ProfileUrl(first.Profile.Id));
        var error = await AssertProblemAsync(errorResponse, HttpStatusCode.NotFound, "Doctor.PublicProfileNotFound");
        AssertFields(error, ["title", "status", "code"]);
        error.GetRawText().ShouldNotContain(first.Profile.PhoneNumber.Value);
        error.GetRawText().ShouldNotContain(first.Profile.LicenseNumber);
    }

    [Theory]
    [InlineData("jwt-invalido")]
    [InlineData("jwt-vencido")]
    [InlineData("roles-duplicados")]
    [InlineData("revocada")]
    [InlineData("vencida")]
    [InlineData("rol-cambiado")]
    public async Task Una_identidad_sin_privilegios_vigentes_solo_recibe_el_contrato_publico(string state)
    {
        var catalog = await SeedCatalogAsync();
        var doctor = await SeedDoctorAsync(catalog.FirstLocation, [catalog.Cardio.Id]);
        var administrator = await LoginAsAsync(UserRole.Administrator);
        var token = administrator.Token;
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        switch (state)
        {
            case "jwt-invalido":
                var parts = token.Split('.');
                parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
                token = string.Join('.', parts);
                break;
            case "jwt-vencido":
                var options = Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
                var now = Clock.UtcNow;
                var issued = now.AddMinutes(-options.AccessTokenMinutes).AddSeconds(-options.ClockSkewSeconds - 60);
                token = CreateAdministratorToken(administrator, issued, nameof(UserRole.Administrator));
                var expired = await new JsonWebTokenHandler().ValidateTokenAsync(token, BearerValidationParameters());
                expired.IsValid.ShouldBeFalse();
                expired.Exception.ShouldBeOfType<SecurityTokenExpiredException>();
                (await db.Users.Where(user => user.Id == administrator.UserId && user.IsActive && user.Role == UserRole.Administrator)
                    .SelectMany(user => user.Sessions).AnyAsync(session => session.Id == administrator.SessionId
                        && session.ExpiresAt > now && session.RevokedAt == null)).ShouldBeTrue();
                break;
            case "roles-duplicados":
                token = CreateAdministratorToken(administrator, Clock.UtcNow,
                    new[] { nameof(UserRole.Administrator), nameof(UserRole.Patient) });
                var claims = await new JsonWebTokenHandler().ValidateTokenAsync(token, BearerValidationParameters());
                claims.IsValid.ShouldBeTrue();
                claims.ClaimsIdentity.FindAll(AppClaims.Role).Select(claim => claim.Value)
                    .ShouldBe(new[] { nameof(UserRole.Administrator), nameof(UserRole.Patient) }, ignoreOrder: true);
                break;
            case "revocada":
                var reason = "logout";
                await db.Database.ExecuteSqlAsync($"""
                    UPDATE app.user_session SET revoked_at = {Clock.UtcNow}, revocation_reason = {reason}
                    WHERE id = {administrator.SessionId}
                    """);
                break;
            case "vencida":
                Clock.UtcNow = Clock.UtcNow.AddSeconds(1);
                await db.Database.ExecuteSqlAsync($"""
                    UPDATE app.user_session SET expires_at = {Clock.UtcNow}
                    WHERE id = {administrator.SessionId}
                    """);
                break;
            case "rol-cambiado":
                await db.Users.Where(user => user.Id == administrator.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Role, UserRole.Patient));
                break;
        }

        using var response = await GetAsync(doctor.Profile.Id, token);
        await ReadPublicAsync(response, doctor.Profile.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Un_DoctorId_no_positivo_devuelve_400_con_error_de_validacion(int doctorId)
    {
        using var response = await GetAsync(doctorId);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "Doctor.InvalidDoctorId");
    }

    [Theory]
    [InlineData("me", HttpStatusCode.MethodNotAllowed)]
    [InlineData("texto", HttpStatusCode.NotFound)]
    [InlineData("2147483648", HttpStatusCode.NotFound)]
    public async Task Un_segmento_fuera_de_Int32_no_accede_a_la_nueva_ruta(string segment, HttpStatusCode status)
    {
        using var response = await Client.GetAsync($"/api/doctors/{segment}/profile");

        await response.ShouldHaveStatusAsync(status);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("Doctor.PublicProfileNotFound");
    }

    [Fact]
    public async Task El_mismo_404_cubre_medico_inexistente_inactivo_o_con_cuenta_inactiva()
    {
        var catalog = await SeedCatalogAsync();
        var inactive = await SeedDoctorAsync(catalog.FirstLocation, [catalog.Cardio.Id], doctorActive: false);
        var blocked = await SeedDoctorAsync(catalog.FirstLocation, [catalog.Cardio.Id], accountActive: false);
        var administrator = await LoginAsAsync(UserRole.Administrator);

        foreach (var id in new[] { int.MaxValue, inactive.Profile.Id, blocked.Profile.Id })
        foreach (var token in new[] { null, administrator.Token })
        {
            using var response = await GetAsync(id, token);
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "Doctor.PublicProfileNotFound");
        }
    }

    [Theory]
    [InlineData("excepcion")]
    [InlineData("cancelacion-interna")]
    [InlineData("variante-inesperada")]
    public async Task Un_error_interno_devuelve_ProblemDetails_generico_incluso_si_se_acepta_texto_plano(string failure)
    {
        const string sensitive = "detalle-privado-sintetico-259";
        var logs = new LogProbe();
        var handler = new DelegateHandler((_, _) => failure switch
        {
            "cancelacion-interna" => throw new OperationCanceledException(sensitive),
            "variante-inesperada" => Task.FromResult(Result<DoctorProfileResult>.Success(new UnexpectedProfile(sensitive))),
            _ => throw new InvalidOperationException(sensitive)
        });
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            ReplaceHandler(services, handler);
            services.AddSingleton<ILogEventSink>(logs);
        }));
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileUrl(42));
        request.Headers.Accept.ParseAdd("text/plain");
        using var response = await client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(sensitive);
        var problem = await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "Doctor.ProfileReadFailed");
        problem.GetProperty("title").GetString().ShouldBe("Ocurrió un error inesperado.");
        problem.TryGetProperty("detail", out _).ShouldBeFalse();
        var entry = logs.Errors.ShouldHaveSingleItem();
        entry.Exception.ShouldBeNull();
        entry.RenderMessage().ShouldNotContain(sensitive);
        entry.RenderMessage().ShouldContain(failure == "cancelacion-interna"
            ? nameof(OperationCanceledException) : nameof(InvalidOperationException));
        logs.Entries.Any(log => log.RenderMessage().Contains(sensitive)
            || log.Exception?.ToString().Contains(sensitive) == true).ShouldBeFalse();
    }

    [Fact]
    public async Task La_cancelacion_del_cliente_llega_al_handler_sin_registrarse_como_500()
    {
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<Result<DoctorProfileResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logs = new LogProbe();
        var handler = new DelegateHandler(async (query, ct) =>
        {
            entered.TrySetResult(query.DoctorId);
            try { return await pending.Task.WaitAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                stopped.TrySetResult();
                throw;
            }
        });
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            ReplaceHandler(services, handler);
            services.AddSingleton<ILogEventSink>(logs);
            services.AddSingleton<IStartupFilter>(new RequestObserver(completed));
        }));
        using var client = app.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var request = client.GetAsync(ProfileUrl(42), cancellation.Token);
        try
        {
            using var premature = await Task.WhenAny(entered.Task, request).WaitAsync(deadline.Token) == request
                ? await request : null;
            premature.ShouldBeNull();
            (await entered.Task.WaitAsync(deadline.Token)).ShouldBe(42);
            await cancellation.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(async () => await request);
            await stopped.Task.WaitAsync(deadline.Token);
            await completed.Task.WaitAsync(deadline.Token);
            logs.Errors.ShouldBeEmpty();
        }
        finally
        {
            await cancellation.CancelAsync();
            pending.TrySetCanceled();
        }
    }

    [Fact]
    public async Task El_GET_anonimo_ve_el_PUT_confirmado_de_inmediato_sin_alterar_identidad_ni_especialidades()
    {
        var catalog = await SeedCatalogAsync();
        var doctor = await SeedDoctorAsync(catalog.FirstLocation, [catalog.Pediatric.Id, catalog.Cardio.Id]);
        using var anonymous = Factory.CreateClient();
        using var authenticated = Factory.CreateClient();
        var url = ProfileUrl(doctor.Profile.Id);
        using var first = await anonymous.GetAsync(url);
        var before = await ReadPublicAsync(first, doctor.Profile.Id);
        using var loginResponse = await LoginAsync(doctor.Email, Password, authenticated);
        await loginResponse.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var login = await loginResponse.ReadLoginAsync();
        authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var update = new
        {
            phoneNumber = "809-555-0160", catalog.SecondLocation.ProvinceId, catalog.SecondLocation.MunicipalityId,
            addressLine = "Avenida nueva 259", consultationFee = 41.20m, yearsOfExperience = 12, biography = "Biografía nueva"
        };
        using var put = await authenticated.PutAsJsonAsync("/api/doctors/me/profile", update);
        await put.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await put.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();

        using var next = await anonymous.GetAsync(url);
        var after = await ReadPublicAsync(next, doctor.Profile.Id);
        after.GetProperty("fullName").GetString().ShouldBe(before.GetProperty("fullName").GetString());
        after.GetProperty("specialties").GetRawText().ShouldBe(before.GetProperty("specialties").GetRawText());
        AssertLocation(after, catalog.SecondLocation);
        after.GetProperty("addressLine").GetString().ShouldBe(update.addressLine);
        after.GetProperty("consultationFee").GetDecimal().ShouldBe(update.consultationFee);
        after.GetProperty("yearsOfExperience").GetInt32().ShouldBe(update.yearsOfExperience);
        after.GetProperty("biography").GetString().ShouldBe(update.biography);
        after.GetProperty("addressLine").GetString().ShouldNotBe(before.GetProperty("addressLine").GetString());
    }

    [Fact]
    public async Task OpenAPI_describe_las_dos_variantes_y_Bearer_opcional_sin_cambiar_las_rutas_existentes()
    {
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })));
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var document = await ReadJsonAsync(response);
        var paths = document.GetProperty("paths");
        var get = paths.GetProperty("/api/doctors/{doctorId}/profile").GetProperty("get");
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == "api/doctors/{doctorId:int}/profile");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldBeEmpty();
        get.GetProperty("parameters").EnumerateArray().ShouldContain(parameter =>
            parameter.GetProperty("name").GetString() == "doctorId"
            && parameter.GetProperty("in").GetString() == "path"
            && parameter.GetProperty("required").GetBoolean()
            && parameter.GetProperty("schema").GetProperty("type").GetString() == "integer");
        var security = get.GetProperty("security").EnumerateArray().ToArray();
        security.Length.ShouldBe(2);
        security[0].EnumerateObject().ShouldBeEmpty();
        security[1].EnumerateObject().ShouldHaveSingleItem().Name.ShouldBe("Bearer");
        security[1].GetProperty("Bearer").EnumerateArray().ShouldBeEmpty();

        var responses = get.GetProperty("responses");
        var expected = new[] { "DoctorPublicProfileResponse", "DoctorAdminProfileResponse" };
        var variants = responses.GetProperty("200").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("oneOf").EnumerateArray();
        variants.Select(value => value.GetProperty("$ref").GetString())
            .ShouldBe(expected.Select(name => "#/components/schemas/" + name), ignoreOrder: true);
        var schemas = document.GetProperty("components").GetProperty("schemas");
        for (var index = 0; index < expected.Length; index++)
        {
            var schema = schemas.GetProperty(expected[index]);
            var fields = index == 0 ? PublicFields : AdminFields;
            AssertFields(schema.GetProperty("properties"), fields);
            schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ShouldBe(fields, ignoreOrder: true);
        }
        schemas.GetProperty(expected[1]).GetProperty("properties").GetProperty("profile").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/" + expected[0]);
        foreach (var status in new[] { "400", "404", "500" })
            responses.GetProperty(status).GetProperty("content").GetProperty("application/problem+json")
                .GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/ProblemDetails");

        foreach (var (path, verb) in new[]
                 {
                     ("/api/doctors/profile", "post"), ("/api/doctors/me/profile", "put"),
                     ("/api/doctors/me/specialties/{specialtyId}", "post"),
                     ("/api/doctors/me/specialties/{specialtyId}", "delete")
                 })
        {
            var protectedOperation = paths.GetProperty(path).GetProperty(verb);
            protectedOperation.GetProperty("security").EnumerateArray().ShouldHaveSingleItem()
                .TryGetProperty("Bearer", out _).ShouldBeTrue();
        }
    }

    private async Task<Catalog> SeedCatalogAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locations = await db.Database.SqlQuery<Location>($"""
            SELECT DISTINCT ON (p.id) p.id AS province_id, p.name AS province_name,
                   m.id AS municipality_id, m.name AS municipality_name
            FROM people.province p JOIN people.municipality m ON m.province_id = p.id
            ORDER BY p.id, m.id LIMIT 2
            """).ToListAsync();
        var pediatric = Specialty.Create("Cardiología Pediátrica", "Especialidad activa.").Value;
        var cardio = Specialty.Create("Cardiología", "Especialidad activa.").Value;
        db.Specialties.AddRange(pediatric, cardio);
        await db.SaveChangesAsync();
        return new(locations[0], locations[1], cardio, pediatric);
    }

    private async Task<DoctorAccount> SeedDoctorAsync(Location location, int[] specialtyIds,
        string phone = "809-555-0111", bool doctorActive = true, bool accountActive = true)
    {
        var email = $"doctor259-{Guid.NewGuid():N}@example.com";
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create(email).Value,
            passwordHash: Factory.HashPassword(Password));
        if (!accountActive) user.Deactivate().IsSuccess.ShouldBeTrue();
        await SaveAsync(user);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = DoctorMother.Create(specialtyIds, userId: user.Id, firstName: "Ana  María",
            lastName: "Núñez", provinceId: location.ProvinceId, municipalityId: location.MunicipalityId,
            phoneNumber: phone, licenseNumber: $"LIC-259-{user.Id}").Value;
        if (!doctorActive) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        return new(doctor, email);
    }

    private async Task<Viewer> LoginAsAsync(UserRole role)
    {
        var email = $"viewer259-{Guid.NewGuid():N}@example.com";
        await SaveUserAsync(role, email, Password, false);
        var login = await LoginSuccessfullyAsync(email, Password);
        var user = (await FindUserAsync(email)).ShouldNotBeNull();
        return new(user.Id, user.Sessions.ShouldHaveSingleItem().Id, login.AccessToken.ShouldNotBeNull());
    }

    private TokenValidationParameters BearerValidationParameters() =>
        Factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

    private string CreateAdministratorToken(Viewer viewer, DateTimeOffset issued, object roleClaim)
    {
        var jwt = Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = issued.UtcDateTime,
            NotBefore = issued.UtcDateTime,
            Expires = issued.AddMinutes(jwt.AccessTokenMinutes).UtcDateTime,
            SigningCredentials = new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey!)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [AppClaims.UserId] = viewer.UserId.ToString(CultureInfo.InvariantCulture),
                [AppClaims.SessionId] = viewer.SessionId.ToString(CultureInfo.InvariantCulture),
                [AppClaims.Role] = roleClaim
            }
        });
    }

    private async Task<HttpResponseMessage> GetAsync(int doctorId, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileUrl(doctorId));
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    private static string ProfileUrl(int id) => $"/api/doctors/{id}/profile";

    private static async Task<JsonElement> ReadPublicAsync(HttpResponseMessage response, int expectedId)
    {
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        AssertNeverStored(response);
        var json = await ReadJsonAsync(response);
        AssertFields(json, PublicFields);
        json.GetProperty("id").GetInt32().ShouldBe(expectedId);
        foreach (var reference in json.GetProperty("specialties").EnumerateArray()
                     .Append(json.GetProperty("province")).Append(json.GetProperty("municipality")))
            AssertFields(reference, ["id", "name"]);
        return json;
    }

    private static void AssertLocation(JsonElement json, Location location)
    {
        json.GetProperty("province").GetProperty("id").GetInt32().ShouldBe(location.ProvinceId);
        json.GetProperty("province").GetProperty("name").GetString().ShouldBe(location.ProvinceName);
        json.GetProperty("municipality").GetProperty("id").GetInt32().ShouldBe(location.MunicipalityId);
        json.GetProperty("municipality").GetProperty("name").GetString().ShouldBe(location.MunicipalityName);
    }

    private static void AssertNeverStored(HttpResponseMessage response)
    {
        var policy = response.Headers.CacheControl.ShouldNotBeNull();
        policy.NoStore.ShouldBeTrue();
        policy.NoCache.ShouldBeTrue();
        policy.Public.ShouldBeFalse();
        policy.SharedMaxAge.ShouldBeNull();
        response.Headers.Pragma.Select(directive => directive.Name).ShouldContain("no-cache");
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await response.ShouldHaveStatusAsync(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        AssertNeverStored(response);
        var problem = await ReadJsonAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("code").GetString().ShouldBe(code);
        return problem;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static void AssertFields(JsonElement json, string[] fields) =>
        json.EnumerateObject().Select(field => field.Name).ShouldBe(fields, ignoreOrder: true);

    private static void ReplaceHandler(IServiceCollection services, DelegateHandler handler)
    {
        services.RemoveAll<IQueryHandler<GetDoctorProfileQuery, DoctorProfileResult>>();
        services.AddSingleton<IQueryHandler<GetDoctorProfileQuery, DoctorProfileResult>>(handler);
    }

    private sealed record Location(int ProvinceId, string ProvinceName, int MunicipalityId, string MunicipalityName);
    private sealed record Catalog(Location FirstLocation, Location SecondLocation, Specialty Cardio, Specialty Pediatric);
    private sealed record DoctorAccount(Doctor Profile, string Email);
    private sealed record Viewer(int UserId, int SessionId, string Token);
    private sealed record UnexpectedProfile(string PrivateDetail) : DoctorProfileResult;

    private sealed class DelegateHandler(Func<GetDoctorProfileQuery, CancellationToken, Task<Result<DoctorProfileResult>>> handler)
        : IQueryHandler<GetDoctorProfileQuery, DoctorProfileResult>
    {
        public Task<Result<DoctorProfileResult>> HandleAsync(GetDoctorProfileQuery query, CancellationToken cancellationToken) =>
            handler(query, cancellationToken);
    }

    private sealed class LogProbe : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Entries { get; } = new();

        public IEnumerable<LogEvent> Errors => Entries.Where(logEvent => logEvent.Level == LogEventLevel.Error
            && logEvent.Properties.TryGetValue("SourceContext", out var source)
            && source is ScalarValue { Value: "SGCM.Web.Api.Controllers.People.DoctorProfilesController" });

        public void Emit(LogEvent logEvent) => Entries.Enqueue(logEvent);
    }

    private sealed class RequestObserver(TaskCompletionSource completed) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> configure) => app =>
        {
            app.Use(async (_, next) =>
            {
                try { await next(); }
                finally { completed.TrySetResult(); }
            });
            configure(app);
        };
    }
}
