using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
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
using Serilog.Core;
using Serilog.Events;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.SearchDoctors;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Api.Controllers.People;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class DoctorSearchHttpTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string SearchUrl = "/api/doctors";
    private const string Password = "Sintetica235";
    private static readonly string[] PageFields = ["items", "pageNumber", "pageSize", "totalCount"];
    private static readonly string[] DoctorFields = ["id", "fullName", "specialties", "province", "municipality", "consultationFee"];

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

    [Fact]
    public async Task El_GET_anonimo_devuelve_la_pagina_por_defecto()
    {
        await ReadPageAsync(SearchUrl, pageNumber: 1, pageSize: 20, totalCount: 0);
    }

    [Fact]
    public async Task Los_filtros_y_la_paginacion_HTTP_conservan_la_elegibilidad_y_todas_las_especialidades_activas()
    {
        var catalog = await SeedCatalogAsync();
        var specialties = new[] { catalog.Pediatric.Id, catalog.Inactive.Id, catalog.Cardio.Id };
        var first = await SeedDoctorAsync(catalog.Main, specialties, lastName: "Alba");
        var second = await SeedDoctorAsync(catalog.Main, specialties, lastName: "Bravo");
        await SeedDoctorAsync(catalog.Main, specialties, firstName: "Elisa");
        await SeedDoctorAsync(catalog.Main, [catalog.Pediatric.Id]);
        await SeedDoctorAsync(catalog.Neighbor, specialties);
        await SeedDoctorAsync(catalog.OtherProvince, specialties);
        await SeedDoctorAsync(catalog.Main, specialties, doctorActive: false);
        await SeedDoctorAsync(catalog.Main, specialties, accountActive: false);
        await SeedDoctorAsync(catalog.Main, [catalog.Inactive.Id]);

        var filters = $"{SearchUrl}?search=ana%20%20maria&specialtyId={catalog.Cardio.Id}&provinceId={catalog.Main.ProvinceId}";
        var combined = $"{filters}&municipalityId={catalog.Main.MunicipalityId}";
        var all = await ReadPageAsync(combined, 1, 20, 2, first.Doctor.Id, second.Doctor.Id);
        AssertDoctor(all.GetProperty("items")[0], first.Doctor, catalog.Main, catalog.Cardio, catalog.Pediatric);
        await ReadPageAsync($"{combined}&pageNumber=1&pageSize=1", 1, 1, 2, first.Doctor.Id);
        await ReadPageAsync($"{combined}&pageNumber=2&pageSize=1", 2, 1, 2, second.Doctor.Id);
        await ReadPageAsync($"{combined}&pageNumber=10&pageSize=1", 10, 1, 2);

        // La combinación incompatible distingue un filtro de provincia omitido de uno redundante.
        await ReadPageAsync($"{filters}&municipalityId={catalog.OtherProvince.MunicipalityId}", 1, 20, 0);
        await ReadPageAsync($"{SearchUrl}?specialtyId={int.MaxValue}", 1, 20, 0);
        await ReadPageAsync($"{SearchUrl}?search=nombre%20inexistente", 1, 20, 0);
    }

    [Theory]
    [InlineData("specialtyId=0", "Doctor.InvalidSpecialtyId")]
    [InlineData("specialtyId=-1", "Doctor.InvalidSpecialtyId")]
    [InlineData("provinceId=0", "Doctor.InvalidProvinceId")]
    [InlineData("provinceId=-1", "Doctor.InvalidProvinceId")]
    [InlineData("municipalityId=0", "Doctor.InvalidMunicipalityId")]
    [InlineData("municipalityId=-1", "Doctor.InvalidMunicipalityId")]
    [InlineData("pageNumber=0", "Doctor.InvalidPageNumber")]
    [InlineData("pageNumber=-1", "Doctor.InvalidPageNumber")]
    [InlineData("pageSize=0", "Doctor.InvalidPageSize")]
    [InlineData("pageSize=101", "Doctor.InvalidPageSize")]
    [InlineData("pageNumber=2147483647&pageSize=2", "Doctor.PageOffsetTooLarge")]
    [InlineData("provinceId=0&provinceId=1", "Doctor.InvalidProvinceId")]
    public async Task Un_valor_enlazado_invalido_conserva_el_400_y_el_codigo_CQRS(string query, string code)
    {
        using var response = await Client.GetAsync($"{SearchUrl}?{query}");

        var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest, code);
        problem.TryGetProperty("errors", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("specialtyId=inválido", "specialtyId")]
    [InlineData("provinceId=inválido", "provinceId")]
    [InlineData("municipalityId=inválido", "municipalityId")]
    [InlineData("specialtyId=2147483648", "specialtyId")]
    [InlineData("provinceId=-2147483649", "provinceId")]
    [InlineData("municipalityId=2147483648", "municipalityId")]
    [InlineData("pageNumber=2147483648", "pageNumber")]
    [InlineData("pageSize=inválido", "pageSize")]
    [InlineData("pageNumber=", "pageNumber")]
    [InlineData("pageSize=", "pageSize")]
    [InlineData("specialtyId=inválido&specialtyId=1", "specialtyId")]
    [InlineData("pageNumber=inválido&pageNumber=1", "pageNumber")]
    public async Task Un_valor_no_convertible_conserva_ValidationProblemDetails_por_campo(string query, string field)
    {
        var calls = 0;
        var handler = new DelegateHandler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Result<PagedResult<DoctorSearchDto>>.Success(new([], 1, 20, 0)));
        });
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ReplaceHandler(services, handler)));
        using var client = app.CreateClient();
        using var response = await client.GetAsync($"{SearchUrl}?{query}");

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await ReadJsonAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe(400);
        problem.TryGetProperty("code", out _).ShouldBeFalse();
        var error = problem.GetProperty("errors").EnumerateObject().ShouldHaveSingleItem();
        error.Name.ShouldBe(field, StringCompareShould.IgnoreCase);
        error.Value.EnumerateArray().ShouldHaveSingleItem().GetString().ShouldNotBeNullOrWhiteSpace();
        Volatile.Read(ref calls).ShouldBe(0);

        using var validResponse = await client.GetAsync(SearchUrl);
        await validResponse.ShouldHaveStatusAsync(HttpStatusCode.OK);
        Volatile.Read(ref calls).ShouldBe(1);
    }

    [Fact]
    public async Task Valores_opcionales_vacios_y_escalares_repetidos_siguen_el_enlace_de_ASP_NET_Core()
    {
        var catalog = await SeedCatalogAsync();
        var doctor = (await SeedDoctorAsync(catalog.Main, [catalog.Cardio.Id])).Doctor;

        await ReadPageAsync($"{SearchUrl}?search=&specialtyId=&provinceId=&municipalityId=", 1, 20, 1, doctor.Id);
        await ReadPageAsync($"{SearchUrl}?search=%20%20", 1, 20, 1, doctor.Id);

        var repeated = $"{SearchUrl}?search=ana&search=inexistente&specialtyId={catalog.Cardio.Id}&specialtyId=inválido"
            + $"&provinceId={catalog.Main.ProvinceId}&provinceId=0&municipalityId={catalog.Main.MunicipalityId}&municipalityId=0"
            + "&pageNumber=1&pageNumber=2&pageSize=3&pageSize=inválido";
        await ReadPageAsync(repeated, 1, 3, 1, doctor.Id);
    }

    [Fact]
    public async Task Todos_los_actores_reciben_la_misma_proyeccion_publica_sin_cambiar_sesiones_ni_datos()
    {
        var catalog = await SeedCatalogAsync();
        var entry = await SeedDoctorAsync(catalog.Main, [catalog.Cardio.Id]);
        const string patientEmail = "paciente235@example.com";
        const string adminEmail = "admin235@example.com";
        const string pendingEmail = "primer-acceso235@example.com";
        await CreateConfirmedPatientAsync(patientEmail, Password);
        await Factory.InsertPatientProfileAsync(patientEmail);
        await SaveUserAsync(UserRole.Administrator, adminEmail, Password, mustChangePassword: false);
        await SaveUserAsync(UserRole.Doctor, pendingEmail, Password, mustChangePassword: true);
        var tokens = new[]
        {
            await LoginAsAsync(patientEmail, UserRole.Patient),
            await LoginAsAsync(entry.Email, UserRole.Doctor),
            await LoginAsAsync(adminEmail, UserRole.Administrator),
            await LoginAsAsync(pendingEmail, UserRole.Doctor, PendingStepClaimValues.ChangeTemporaryPassword),
            "jwt-invalido"
        };
        var before = await ReadStateAsync();
        before.Sessions.Count.ShouldBe(4);
        Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
        var url = $"{SearchUrl}?search=ana";

        var anonymousPage = await ReadPageAsync(url, 1, 20, 1, entry.Doctor.Id);
        AssertDoctor(anonymousPage.GetProperty("items")[0], entry.Doctor, catalog.Main, catalog.Cardio);
        await AssertUnchangedAsync();
        foreach (var token in tokens)
        {
            var samePage = await ReadPageAsync(url, 1, 20, 1, [entry.Doctor.Id], token);
            samePage.GetRawText().ShouldBe(anonymousPage.GetRawText());
            await AssertUnchangedAsync();
        }

        async Task AssertUnchangedAsync()
        {
            var after = await ReadStateAsync();
            after.Sessions.SequenceEqual(before.Sessions).ShouldBeTrue();
            after.Directory.ShouldBe(before.Directory);
        }
    }

    [Fact]
    public async Task El_anonimo_abre_el_perfil_publico_del_medico_encontrado_por_nombre()
    {
        var catalog = await SeedCatalogAsync();
        var (doctor, _) = await SeedDoctorAsync(catalog.Main, [catalog.Cardio.Id, catalog.Pediatric.Id]);
        var page = await ReadPageAsync($"{SearchUrl}?search=maria%20nunez", 1, 20, 1, doctor.Id);
        var found = page.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        var id = found.GetProperty("id").GetInt32();

        using var response = await Client.GetAsync($"{SearchUrl}/{id}/profile");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var profile = await ReadJsonAsync(response);
        AssertFields(profile, [.. DoctorFields, "addressLine", "yearsOfExperience", "biography"]);
        AssertDoctorValues(profile, doctor, catalog.Main, catalog.Cardio, catalog.Pediatric);
        profile.GetProperty("addressLine").GetString().ShouldBe(doctor.AddressLine);
        profile.GetProperty("yearsOfExperience").GetInt32().ShouldBe(doctor.YearsOfExperience);
        profile.GetProperty("biography").GetString().ShouldBe(doctor.Biography);
    }

    [Theory]
    [InlineData("excepcion", nameof(InvalidOperationException))]
    [InlineData("cancelacion-interna", nameof(OperationCanceledException))]
    [InlineData("especialidades-inconsistentes", nameof(InvalidOperationException))]
    public async Task Los_fallos_internos_devuelven_500_y_solo_registran_diagnostico_seguro(string failure, string exceptionType)
    {
        const string sensitive = "detalle-privado-sintetico-235";
        var log = new LogProbe();
        var handler = new DelegateHandler((_, _) => failure switch
        {
            "cancelacion-interna" => throw new OperationCanceledException(sensitive),
            "especialidades-inconsistentes" => Task.FromResult(Result<PagedResult<DoctorSearchDto>>.Success(
                new([new(1, "Ana", "Núñez", 19, "Sánchez Ramírez", 31, "Cotuí", sensitive, 100m, 1, sensitive,
                    [3], [])], 1, 20, 1))),
            _ => throw new InvalidOperationException(sensitive)
        });
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            ReplaceHandler(services, handler);
            services.AddSingleton<ILogEventSink>(log);
        }));
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, SearchUrl);
        request.Headers.Accept.ParseAdd("text/plain");
        using var response = await client.SendAsync(request);

        var problem = await ReadProblemAsync(response, HttpStatusCode.InternalServerError, "Doctor.SearchFailed");
        AssertFields(problem, "title", "status", "code");
        problem.GetProperty("title").GetString().ShouldBe("Ocurrió un error inesperado.");
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(sensitive);
        var entry = log.SearchErrors.ShouldHaveSingleItem();
        entry.Properties["ExceptionType"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(exceptionType);
        entry.Properties["TraceId"].ShouldBeOfType<ScalarValue>().Value.ShouldBeOfType<string>().ShouldNotBeNullOrWhiteSpace();
        entry.Exception.ShouldBeNull();
        log.Entries.Any(item => item.RenderMessage().Contains(sensitive, StringComparison.Ordinal)
            || item.Exception?.ToString().Contains(sensitive, StringComparison.Ordinal) == true).ShouldBeFalse();
    }

    [Fact]
    public async Task La_cancelacion_del_cliente_llega_al_handler_sin_convertirse_en_500()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<Result<PagedResult<DoctorSearchDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new LogProbe();
        var handler = new DelegateHandler(async (_, ct) =>
        {
            entered.TrySetResult(ct);
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
            services.AddSingleton<ILogEventSink>(log);
            services.AddSingleton<IStartupFilter>(new RequestObserver(completed));
        }));
        using var client = app.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var request = client.GetAsync(SearchUrl, cancellation.Token);
        try
        {
            using var premature = await Task.WhenAny(entered.Task, request).WaitAsync(deadline.Token) == request
                ? await request : null;
            premature.ShouldBeNull();
            var handlerToken = await entered.Task.WaitAsync(deadline.Token);
            handlerToken.CanBeCanceled.ShouldBeTrue();
            handlerToken.IsCancellationRequested.ShouldBeFalse();
            await cancellation.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(async () => await request);
            await stopped.Task.WaitAsync(deadline.Token);
            await completed.Task.WaitAsync(deadline.Token);
            handlerToken.IsCancellationRequested.ShouldBeTrue();
            log.SearchErrors.ShouldBeEmpty();
        }
        finally
        {
            await cancellation.CancelAsync();
            pending.TrySetCanceled();
        }
    }

    [Fact]
    public async Task OpenAPI_describe_la_busqueda_publica_y_conserva_las_rutas_vecinas()
    {
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })));
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var document = await ReadJsonAsync(response);
        var paths = document.GetProperty("paths");
        var operation = paths.GetProperty(SearchUrl).GetProperty("get");
        (operation.TryGetProperty("security", out var security) && security.GetArrayLength() != 0).ShouldBeFalse();
        paths.TryGetProperty($"{SearchUrl}/search", out _).ShouldBeFalse();
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == "api/doctors");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldBeEmpty();

        var parameters = operation.GetProperty("parameters").EnumerateArray().ToDictionary(
            parameter => parameter.GetProperty("name").GetString().ShouldNotBeNull());
        parameters.Keys.ShouldBe(new[] { "search", "specialtyId", "provinceId", "municipalityId", "pageNumber", "pageSize" },
            ignoreOrder: true);
        foreach (var (name, parameter) in parameters)
        {
            parameter.GetProperty("in").GetString().ShouldBe("query");
            (parameter.TryGetProperty("required", out var required) && required.GetBoolean()).ShouldBeFalse();
            var schema = parameter.GetProperty("schema");
            schema.GetProperty("type").GetString().ShouldBe(name == "search" ? "string" : "integer");
            if (name != "search") schema.GetProperty("format").GetString().ShouldBe("int32");
        }
        parameters["pageNumber"].GetProperty("schema").GetProperty("default").GetInt32().ShouldBe(1);
        parameters["pageSize"].GetProperty("schema").GetProperty("default").GetInt32().ShouldBe(20);

        var responses = operation.GetProperty("responses");
        AssertFields(responses, "200", "400", "500");
        ResponseSchema(responses, "200", "application/json").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/SearchDoctorsResponse");
        ResponseSchema(responses, "500", "application/problem+json").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/ProblemDetails");
        var invalid = ResponseSchema(responses, "400", "application/problem+json").GetProperty("oneOf").EnumerateArray()
            .ToDictionary(schema => schema.GetProperty("required").EnumerateArray().ShouldHaveSingleItem().GetString().ShouldNotBeNull());
        invalid.Keys.ShouldBe(new[] { "code", "errors" }, ignoreOrder: true);
        invalid["errors"].GetProperty("allOf").EnumerateArray().ShouldHaveSingleItem().GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/ValidationProblemDetails");
        invalid["code"].GetProperty("allOf").EnumerateArray().ShouldHaveSingleItem().GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/ProblemDetails");
        invalid["code"].GetProperty("properties").GetProperty("code").GetProperty("type").GetString().ShouldBe("string");

        var schemas = document.GetProperty("components").GetProperty("schemas");
        var page = schemas.GetProperty("SearchDoctorsResponse").GetProperty("properties");
        AssertFields(page, PageFields);
        page.GetProperty("items").GetProperty("items").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/DoctorSearchResponse");
        AssertFields(schemas.GetProperty("DoctorSearchResponse").GetProperty("properties"), DoctorFields);

        foreach (var (path, verb) in new[]
        {
            ("/api/doctors/{doctorId}/profile", "get"), ("/api/doctors/profile", "post"),
            ("/api/doctors/me/profile", "put"), ("/api/doctors/me/specialties/{specialtyId}", "post"),
            ("/api/doctors/me/specialties/{specialtyId}", "delete"), ("/api/doctors/{doctorId}/available-slots", "get"),
            ("/api/doctors/me/available-slots", "get"), ("/api/doctors/me/availability-blocks", "post"),
            ("/api/doctors/me/availability-series", "post"), ("/api/doctors/me/appointments/{id}/cancel", "post")
        })
        {
            paths.GetProperty(path).TryGetProperty(verb, out _).ShouldBeTrue();
        }
    }

    private async Task<Catalog> SeedCatalogAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locations = await db.Database.SqlQuery<Location>($"""
            SELECT p.id AS province_id, p.name AS province_name,
                   m.id AS municipality_id, m.name AS municipality_name
            FROM people.province p JOIN people.municipality m ON m.province_id = p.id
            ORDER BY p.id, m.id
            """).ToListAsync();
        locations.Count.ShouldBe(158);
        var provinces = locations.GroupBy(location => location.ProvinceId).ToArray();
        provinces.Length.ShouldBe(32);
        var main = provinces.First(province => province.Skip(1).Any()).Take(2).ToArray();
        var other = provinces.First(province => province.Key != main[0].ProvinceId).First();
        var pediatric = Specialty.Create("Pediatría", "Especialidad activa.").Value;
        var cardio = Specialty.Create("Cardiología", "Especialidad activa.").Value;
        var inactive = Specialty.Create("Neurología", "Especialidad inactiva.").Value;
        inactive.Deactivate().IsSuccess.ShouldBeTrue();
        db.Specialties.AddRange(pediatric, cardio, inactive);
        await db.SaveChangesAsync();
        return new(main[0], main[1], other, cardio, pediatric, inactive);
    }

    private async Task<(Doctor Doctor, string Email)> SeedDoctorAsync(Location location, int[] specialties,
        string firstName = "Ana  María", string lastName = "Núñez", bool doctorActive = true, bool accountActive = true)
    {
        var email = $"doctor235-{Guid.NewGuid():N}@example.com";
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create(email).Value, passwordHash: Factory.HashPassword(Password));
        if (!accountActive) user.Deactivate().IsSuccess.ShouldBeTrue();
        await SaveAsync(user);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = DoctorMother.Create(specialties, userId: user.Id, firstName: firstName, lastName: lastName,
            provinceId: location.ProvinceId, municipalityId: location.MunicipalityId, licenseNumber: $"LIC-235-{user.Id}").Value;
        if (!doctorActive) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        return (doctor, email);
    }

    private async Task<string> LoginAsAsync(string email, UserRole role, string? pendingStep = null)
    {
        var login = await LoginSuccessfullyAsync(email, Password);
        login.PendingStep.ShouldBe(pendingStep);
        var token = login.AccessToken.ShouldNotBeNull();
        var parameters = Factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        (await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters)).IsValid.ShouldBeTrue();
        var jwt = new JsonWebToken(token);
        jwt.GetClaim(AppClaims.Role).Value.ShouldBe(role.ToString());
        var user = (await FindUserAsync(email)).ShouldNotBeNull();
        jwt.GetClaim(AppClaims.UserId).Value.ShouldBe(user.Id.ToString(CultureInfo.InvariantCulture));
        var session = user.Sessions.ShouldHaveSingleItem();
        jwt.GetClaim(AppClaims.SessionId).Value.ShouldBe(session.Id.ToString(CultureInfo.InvariantCulture));
        session.IsActiveAt(Clock.UtcNow).ShouldBeTrue();
        (jwt.TryGetClaim(AppClaims.PendingStep, out var claim) ? claim.Value : null).ShouldBe(pendingStep);
        return token;
    }

    private async Task<(List<string> Sessions, string Directory)> ReadStateAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sessions = await db.Users.SelectMany(user => user.Sessions).OrderBy(session => session.Id)
            .Select(session => new
            {
                session.Id, session.RefreshTokenHash, session.PreviousRefreshTokenHash, session.DeviceName, session.IpAddress,
                session.ExpiresAt, session.LastUsedAt, session.RevokedAt, session.RevocationReason
            }).ToListAsync();
        var directory = await db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_array(
                (SELECT jsonb_agg(to_jsonb(d) ORDER BY d.id) FROM people.doctor d),
                (SELECT jsonb_agg(to_jsonb(s) ORDER BY s.id) FROM people.specialty s),
                (SELECT jsonb_agg(to_jsonb(ds) ORDER BY ds.doctor_id, ds.specialty_id) FROM people.doctor_specialty ds)
            )::text AS "Value"
            """).SingleAsync();
        return (sessions.Select(session => JsonSerializer.Serialize(session)).ToList(), directory);
    }

    private Task<JsonElement> ReadPageAsync(string url, int pageNumber, int pageSize, int totalCount, params int[] ids) =>
        ReadPageAsync(url, pageNumber, pageSize, totalCount, ids, token: null);

    private async Task<JsonElement> ReadPageAsync(string url, int pageNumber, int pageSize, int totalCount, int[] ids, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Client.SendAsync(request);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        var page = await ReadJsonAsync(response);
        AssertFields(page, PageFields);
        page.GetProperty("pageNumber").GetInt32().ShouldBe(pageNumber);
        page.GetProperty("pageSize").GetInt32().ShouldBe(pageSize);
        page.GetProperty("totalCount").GetInt32().ShouldBe(totalCount);
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt32()).ShouldBe(ids);
        return page;
    }

    private static void AssertDoctor(JsonElement json, Doctor doctor, Location location, params Specialty[] specialties)
    {
        AssertFields(json, DoctorFields);
        AssertDoctorValues(json, doctor, location, specialties);
    }

    private static void AssertDoctorValues(JsonElement json, Doctor doctor, Location location, params Specialty[] specialties)
    {
        json.GetProperty("id").GetInt32().ShouldBe(doctor.Id);
        json.GetProperty("fullName").GetString().ShouldBe($"{doctor.FirstName} {doctor.LastName}");
        json.GetProperty("consultationFee").GetDecimal().ShouldBe(doctor.ConsultationFee.Value);
        ReadReference(json.GetProperty("province")).ShouldBe((location.ProvinceId, location.ProvinceName));
        ReadReference(json.GetProperty("municipality")).ShouldBe((location.MunicipalityId, location.MunicipalityName));
        json.GetProperty("specialties").EnumerateArray().Select(ReadReference)
            .ShouldBe(specialties.Select(specialty => (specialty.Id, specialty.Name)));
    }

    private static (int Id, string Name) ReadReference(JsonElement reference)
    {
        AssertFields(reference, "id", "name");
        return (reference.GetProperty("id").GetInt32(), reference.GetProperty("name").GetString().ShouldNotBeNull());
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await response.ShouldHaveStatusAsync(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await ReadJsonAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("code").GetString().ShouldBe(code);
        return problem;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static void AssertFields(JsonElement json, params string[] fields) =>
        json.EnumerateObject().Select(field => field.Name).ShouldBe(fields, ignoreOrder: true);

    private static JsonElement ResponseSchema(JsonElement responses, string status, string contentType) =>
        responses.GetProperty(status).GetProperty("content").GetProperty(contentType).GetProperty("schema");

    private static void ReplaceHandler(IServiceCollection services, DelegateHandler handler)
    {
        services.RemoveAll<IQueryHandler<SearchDoctorsQuery, PagedResult<DoctorSearchDto>>>();
        services.AddSingleton<IQueryHandler<SearchDoctorsQuery, PagedResult<DoctorSearchDto>>>(handler);
    }

    private sealed record Location(int ProvinceId, string ProvinceName, int MunicipalityId, string MunicipalityName);
    private sealed record Catalog(Location Main, Location Neighbor, Location OtherProvince, Specialty Cardio,
        Specialty Pediatric, Specialty Inactive);

    private sealed class DelegateHandler(
        Func<SearchDoctorsQuery, CancellationToken, Task<Result<PagedResult<DoctorSearchDto>>>> handler)
        : IQueryHandler<SearchDoctorsQuery, PagedResult<DoctorSearchDto>>
    {
        public Task<Result<PagedResult<DoctorSearchDto>>> HandleAsync(SearchDoctorsQuery query, CancellationToken cancellationToken) =>
            handler(query, cancellationToken);
    }

    private sealed class LogProbe : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Entries { get; } = new();

        public IEnumerable<LogEvent> SearchErrors => Entries.Where(entry => entry.Level == LogEventLevel.Error
            && entry.Properties.TryGetValue("SourceContext", out var source)
            && source is ScalarValue { Value: "SGCM.Web.Api.Controllers.People.DoctorSearchController" });

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
