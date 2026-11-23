using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using Serilog.Core;
using Serilog.Events;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.People.Queries.ListMunicipalities;
using SGCM.Data.Application.People.Queries.ListProvinces;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class LocationCatalogHttpTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string ProvincesUrl = "/api/locations/provinces";

    [Fact]
    public async Task GET_anonimo_lista_las_provincias_del_catalogo()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expected = await db.Database.SqlQuery<Location>($"""
            SELECT id, name FROM people.province ORDER BY name, id
            """).ToArrayAsync();

        Client.DefaultRequestHeaders.Authorization.ShouldBeNull();
        using var response = await Client.GetAsync(ProvincesUrl);
        var actual = await ReadLocationsAsync(response);

        actual.ShouldBe(expected);
        actual.ShouldContain(item => item.Name == "Distrito Nacional");
        actual.ShouldContain(item => item.Name == "Dajabón");
    }

    [Fact]
    public async Task GET_anonimo_lista_solo_los_municipios_de_la_provincia()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provinceId = await ReadProvinceIdAsync(db, "Azua");
        var expected = await db.Database.SqlQuery<Location>($"""
            SELECT id, name FROM people.municipality WHERE province_id = {provinceId} ORDER BY name, id
            """).ToArrayAsync();

        Client.DefaultRequestHeaders.Authorization.ShouldBeNull();
        using var response = await Client.GetAsync(MunicipalitiesUrl(provinceId));
        var actual = await ReadLocationsAsync(response);

        actual.ShouldBe(expected);
        actual.ShouldContain(item => item.Name == "Estebanía");
        actual.ShouldContain(item => item.Name == "Tábara Arriba");
        actual.ShouldNotContain(item => item.Name == "Santo Domingo de Guzmán");
    }

    [Fact]
    public async Task GET_distingue_provincia_vacia_de_provincia_inexistente()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await ReadDatabaseStateAsync(db);
        var name = $"Provincia de prueba {Guid.NewGuid():N}";
        int provinceId;
        try
        {
            var inserted = await db.Database.SqlQuery<int>($"""
                INSERT INTO people.province (name) VALUES ({name}) RETURNING id AS "Value"
                """).ToListAsync();
            provinceId = inserted.ShouldHaveSingleItem();

            using var empty = await Client.GetAsync(MunicipalitiesUrl(provinceId));
            (await ReadLocationsAsync(empty)).ShouldBeEmpty();
        }
        finally
        {
            // El host HTTP usa otra conexión. Limpiar por el nombre único incluso si falla la lectura del ID.
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM people.province WHERE name = {name}");
            (await ReadDatabaseStateAsync(db)).ShouldBe(before);
        }

        using var missing = await Client.GetAsync(MunicipalitiesUrl(provinceId));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.ReadErrorCodeAsync()).ShouldBe("Location.ProvinceNotFound");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GET_rechaza_ids_no_positivos(int provinceId)
    {
        using var response = await Client.GetAsync(MunicipalitiesUrl(provinceId));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await response.ReadErrorCodeAsync()).ShouldBe("Location.InvalidProvinceId");
    }

    [Theory]
    [InlineData("azua")]
    [InlineData("2147483648")]
    public async Task GET_no_enruta_un_segmento_que_no_es_un_int(string segment)
    {
        using var response = await Client.GetAsync($"{ProvincesUrl}/{segment}/municipalities");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("Location.ProvinceNotFound");
    }

    [Fact]
    public async Task Sin_JWT_o_con_JWT_valido_o_invalido_la_lectura_es_equivalente_y_no_crea_sesiones()
    {
        const string email = "administrador.catalogo233@correo.com";
        const string password = "CatalogoSintetico233";
        Clock.UtcNow = DateTimeOffset.UtcNow;
        await SaveUserAsync(UserRole.Administrator, email, password, mustChangePassword: false);
        using var loginResponse = await LoginAsync(email, password);
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = await loginResponse.ReadLoginAsync();
        login.PendingStep.ShouldBeNull();
        var token = login.AccessToken.ShouldNotBeNull();
        var bearer = Factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, bearer.TokenValidationParameters);
        validation.IsValid.ShouldBeTrue("El JWT emitido por el login debe ser válido para la API.");
        using var validBearer = Factory.CreateClient();
        validBearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var invalidBearer = Factory.CreateClient();
        invalidBearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var province = await ReadProvinceIdAsync(db, "Distrito Nacional");
        var before = await ReadDatabaseStateAsync(db);
        before.Sessions.ShouldBe(1);

        foreach (var url in new[] { ProvincesUrl, MunicipalitiesUrl(province) })
        {
            using var anonymous = await Client.GetAsync(url);
            var expected = await ReadLocationsAsync(anonymous);
            using var validToken = await validBearer.GetAsync(url);
            using var invalidToken = await invalidBearer.GetAsync(url);
            (await ReadLocationsAsync(validToken)).ShouldBe(expected);
            (await ReadLocationsAsync(invalidToken)).ShouldBe(expected);
        }

        (await ReadDatabaseStateAsync(db)).ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task El_error_interno_incluso_si_es_cancelacion_devuelve_un_500_sin_detalles(bool internalCancellation)
    {
        const string syntheticDetail = "detalle-interno-sintetico-233";
        var logs = new LogProbe();
        var reader = Substitute.For<ILocationCatalogReader>();
        Exception failure = internalCancellation
            ? new OperationCanceledException(syntheticDetail)
            : new InvalidOperationException(syntheticDetail);
        reader.ListProvincesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<ProvinceDto>>(failure));
        reader.ListMunicipalitiesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<MunicipalityDto>?>(failure));
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILocationCatalogReader>();
            services.AddSingleton(reader);
            services.AddSingleton<ILogEventSink>(logs);
        }));
        using var client = app.CreateClient();

        foreach (var url in new[] { ProvincesUrl, MunicipalitiesUrl(1) })
        {
            using var response = await client.GetAsync(url);
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
            var raw = await response.Content.ReadAsStringAsync();
            raw.Contains(syntheticDetail, StringComparison.Ordinal).ShouldBeFalse();
            using var json = JsonDocument.Parse(raw);
            var problem = json.RootElement;
            problem.EnumerateObject().Select(field => field.Name)
                .ShouldBe(new[] { "title", "status", "code" }, ignoreOrder: true);
            problem.GetProperty("title").GetString().ShouldBe("Ocurrió un error inesperado.");
            problem.GetProperty("code").GetString().ShouldBe("Location.CatalogReadFailed");
        }

        var errors = logs.ControllerErrors.ToArray();
        errors.Length.ShouldBe(2);
        foreach (var entry in errors)
        {
            entry.Exception.ShouldBeNull();
            entry.RenderMessage().Contains(syntheticDetail, StringComparison.Ordinal).ShouldBeFalse();
            entry.RenderMessage().ShouldContain(failure.GetType().Name);
        }
    }

    [Fact]
    public async Task La_cancelacion_del_cliente_llega_al_reader_con_el_token_de_la_peticion()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<IReadOnlyList<MunicipalityDto>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = Substitute.For<ILocationCatalogReader>();
        reader.ListMunicipalitiesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => WaitForCancellationAsync(call.Arg<CancellationToken>()));
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILocationCatalogReader>();
            services.AddSingleton(reader);
        }));
        using var client = app.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var request = client.GetAsync(MunicipalitiesUrl(1), cancellation.Token);
        try
        {
            var serverToken = await entered.Task.WaitAsync(deadline.Token);
            serverToken.CanBeCanceled.ShouldBeTrue();
            await cancellation.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(async () => await request);
            await canceled.Task.WaitAsync(deadline.Token);
            serverToken.IsCancellationRequested.ShouldBeTrue();
        }
        finally
        {
            await cancellation.CancelAsync();
            pending.TrySetCanceled();
        }

        async Task<IReadOnlyList<MunicipalityDto>?> WaitForCancellationAsync(CancellationToken token)
        {
            entered.TrySetResult(token);
            try
            {
                return await pending.Task.WaitAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                canceled.TrySetResult();
                throw;
            }
        }
    }

    [Fact]
    public async Task OpenAPI_documenta_listas_publicas_el_parametro_y_los_errores()
    {
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })));
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        var provinces = paths.GetProperty(ProvincesUrl).GetProperty("get");
        var municipalities = paths.GetProperty($"{ProvincesUrl}/{{provinceId}}/municipalities").GetProperty("get");

        AssertPublicOperation(provinces, "ProvinceResponse", ["500"]);
        AssertPublicOperation(municipalities, "MunicipalityResponse", ["400", "404", "500"]);
        var parameter = municipalities.GetProperty("parameters").EnumerateArray().ShouldHaveSingleItem();
        parameter.GetProperty("name").GetString().ShouldBe("provinceId");
        parameter.GetProperty("in").GetString().ShouldBe("path");
        parameter.GetProperty("required").GetBoolean().ShouldBeTrue();
        parameter.GetProperty("schema").GetProperty("type").GetString().ShouldBe("integer");

        void AssertPublicOperation(JsonElement operation, string itemSchema, string[] errorStatuses)
        {
            if (operation.TryGetProperty("security", out var security))
                security.EnumerateArray().ShouldBeEmpty();
            var responses = operation.GetProperty("responses");
            var success = responses.GetProperty("200").GetProperty("content").GetProperty("application/json")
                .GetProperty("schema");
            success.GetProperty("type").GetString().ShouldBe("array");
            success.GetProperty("items").GetProperty("$ref").GetString().ShouldBe($"#/components/schemas/{itemSchema}");
            var fields = schemas.GetProperty(itemSchema).GetProperty("properties");
            fields.EnumerateObject().Select(field => field.Name).ShouldBe(new[] { "id", "name" }, ignoreOrder: true);
            fields.GetProperty("id").GetProperty("type").GetString().ShouldBe("integer");
            fields.GetProperty("name").GetProperty("type").GetString().ShouldBe("string");
            foreach (var status in errorStatuses)
                responses.GetProperty(status).GetProperty("content").GetProperty("application/problem+json")
                    .GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/ProblemDetails");
        }
    }

    private static string MunicipalitiesUrl(int provinceId) => $"{ProvincesUrl}/{provinceId}/municipalities";

    private static Task<DatabaseState> ReadDatabaseStateAsync(AppDbContext db) =>
        db.Database.SqlQuery<DatabaseState>($"""
            SELECT
                (SELECT md5(jsonb_agg(to_jsonb(p) ORDER BY p.id)::text) FROM people.province p) AS provinces,
                (SELECT md5(jsonb_agg(to_jsonb(m) ORDER BY m.id)::text) FROM people.municipality m) AS municipalities,
                (SELECT count(*) FROM people.province) AS province_count,
                (SELECT count(*) FROM people.municipality) AS municipality_count,
                (SELECT count(*) FROM app.user_session) AS sessions
            """).SingleAsync();

    private static Task<int> ReadProvinceIdAsync(AppDbContext db, string name) =>
        db.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM people.province WHERE name = {name}").SingleAsync();

    private static async Task<Location[]> ReadLocationsAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(item =>
        {
            item.EnumerateObject().Select(field => field.Name).ShouldBe(new[] { "id", "name" }, ignoreOrder: true);
            return new Location(item.GetProperty("id").GetInt32(), item.GetProperty("name").GetString().ShouldNotBeNull());
        }).ToArray();
    }

    private sealed record Location(int Id, string Name);
    private sealed record DatabaseState(
        string Provinces, string Municipalities, long ProvinceCount, long MunicipalityCount, long Sessions);

    private sealed class LogProbe : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _entries = new();

        public IEnumerable<LogEvent> ControllerErrors => _entries.Where(entry => entry.Level == LogEventLevel.Error
            && entry.Properties.TryGetValue("SourceContext", out var source)
            && source is ScalarValue { Value: "SGCM.Web.Api.Controllers.People.LocationsController" });

        public void Emit(LogEvent logEvent) => _entries.Enqueue(logEvent);
    }
}
