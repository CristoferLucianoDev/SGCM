using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.People;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class UpdateInsuranceProviderTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string OtherName = "Seguros Nacional";
    private const string NewPhone = "809-555-0999";
    private const string EmailDomain = "@correo.com";

    // El relleno del sitio web va en la ruta para no depender de cómo Uri trata un host muy largo.
    private const string WebsitePrefix = "https://seguros.com/";

    public static TheoryData<string, string?, string, string> MissingRequiredFields
    {
        get
        {
            var data = new TheoryData<string, string?, string, string>();
            foreach (var value in new string?[] { null, "", "   " })
            {
                data.Add("name", value, InsuranceProviderErrors.NameRequired.Code, InsuranceProviderErrors.NameRequired.Description);
                data.Add("contactPhone", value, InsuranceProviderErrors.ContactPhoneRequired.Code, InsuranceProviderErrors.ContactPhoneRequired.Description);
                data.Add("contactEmail", value, CommonErrors.InvalidEmail.Code, CommonErrors.InvalidEmail.Description);
                data.Add("address", value, InsuranceProviderErrors.AddressRequired.Code, InsuranceProviderErrors.AddressRequired.Description);
            }
            return data;
        }
    }

    public static TheoryData<string, string, string, string> InvalidValues => new()
    {
        { "name", new string('a', InsuranceProvider.MaxNameLength + 1), InsuranceProviderErrors.NameTooLong.Code, InsuranceProviderErrors.NameTooLong.Description },
        { "contactPhone", new string('1', InsuranceProvider.MaxContactPhoneLength + 1), InsuranceProviderErrors.ContactPhoneTooLong.Code, InsuranceProviderErrors.ContactPhoneTooLong.Description },
        { "address", new string('b', InsuranceProvider.MaxAddressLength + 1), InsuranceProviderErrors.AddressTooLong.Code, InsuranceProviderErrors.AddressTooLong.Description },
        { "website", WebsiteOfLength(InsuranceProvider.MaxWebsiteLength + 1), InsuranceProviderErrors.WebsiteTooLong.Code, InsuranceProviderErrors.WebsiteTooLong.Description },
        { "contactEmail", "sin-arroba", CommonErrors.InvalidEmail.Code, CommonErrors.InvalidEmail.Description },
        { "contactEmail", new string('a', Email.MaxLength + 1 - EmailDomain.Length) + EmailDomain, CommonErrors.InvalidEmail.Code, CommonErrors.InvalidEmail.Description },
        { "website", "javascript:alert(1)", InsuranceProviderErrors.WebsiteInvalid.Code, InsuranceProviderErrors.WebsiteInvalid.Description },
        { "website", "ftp://x.com", InsuranceProviderErrors.WebsiteInvalid.Code, InsuranceProviderErrors.WebsiteInvalid.Description },
        { "website", "/ruta/relativa", InsuranceProviderErrors.WebsiteInvalid.Code, InsuranceProviderErrors.WebsiteInvalid.Description },
        { "website", "www.sitio.com", InsuranceProviderErrors.WebsiteInvalid.Code, InsuranceProviderErrors.WebsiteInvalid.Description }
    };

    public static TheoryData<bool, bool, bool, bool, bool, string> ErrorPriority => new()
    {
        { true, true, true, true, true, InsuranceProviderErrors.NameRequired.Code },
        { false, true, true, true, true, InsuranceProviderErrors.ContactPhoneRequired.Code },
        { false, false, true, true, true, CommonErrors.InvalidEmail.Code },
        { false, false, false, true, true, InsuranceProviderErrors.AddressRequired.Code },
        { false, false, false, false, true, InsuranceProviderErrors.WebsiteInvalid.Code }
    };

    [Theory]
    [InlineData("name", true)]
    [InlineData("contactPhone", true)]
    [InlineData("contactEmail", true)]
    [InlineData("address", true)]
    [InlineData("website", true)]
    [InlineData("name", false)]
    [InlineData("contactPhone", false)]
    [InlineData("contactEmail", false)]
    [InlineData("address", false)]
    [InlineData("website", false)]
    public async Task Administrator_cambia_un_solo_campo_y_conserva_los_otros_cuatro_el_id_y_el_estado(string field, bool isActive)
    {
        var id = await SeedAsync(isActive: isActive);
        var before = await ReadAllAsync();
        var (sent, normalized) = ChangeOf(field);
        Authorize(UserRole.Administrator);

        using var response = await PatchAsync(id, new Dictionary<string, string?> { [field] = sent });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        await AssertOnlyChangedAsync(before, id, Apply(before.Single(item => item.Id == id), field, normalized));
        (await TouchedIdsAsync()).ShouldBe(new[] { id });
    }

    [Fact]
    public async Task Administrator_cambia_todos_los_campos_con_espacios_y_correo_en_mayusculas_y_se_guardan_normalizados()
    {
        var id = await SeedAsync(isActive: false);
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchAsync(id, new Dictionary<string, string?>
        {
            ["name"] = "  Seguros Nacional  ",
            ["contactPhone"] = "  809-555-0999  ",
            ["contactEmail"] = "  CONTACTO@NACIONAL.COM  ",
            ["address"] = "  Calle Nueva 5, Santiago  ",
            ["website"] = "  https://nacional.com/inicio  "
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await AssertOnlyChangedAsync(before, id, new Snapshot(id, "Seguros Nacional", "809-555-0999",
            "contacto@nacional.com", "Calle Nueva 5, Santiago", "https://nacional.com/inicio", false));
    }

    [Fact]
    public async Task Acepta_las_longitudes_maximas_despues_de_recortar_los_espacios()
    {
        var id = await SeedAsync();
        var before = await ReadAllAsync();
        var name = new string('a', InsuranceProvider.MaxNameLength);
        var phone = new string('1', InsuranceProvider.MaxContactPhoneLength);
        var address = new string('b', InsuranceProvider.MaxAddressLength);
        var website = WebsiteOfLength(InsuranceProvider.MaxWebsiteLength);
        Authorize(UserRole.Administrator);

        using var response = await PatchAsync(id, new Dictionary<string, string?>
        {
            ["name"] = Padded(name), ["contactPhone"] = Padded(phone), ["address"] = Padded(address), ["website"] = Padded(website)
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await AssertOnlyChangedAsync(before, id,
            before.Single().WithTexts(name, phone, address, website));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public async Task Un_sitio_web_nulo_vacio_o_en_blanco_quita_el_sitio_existente_y_conserva_los_demas_datos(string json)
    {
        var id = await SeedAsync();
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchRawAsync(id, $$"""{"website":{{json}}}""");

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await AssertOnlyChangedAsync(before, id, before.Single() with { Website = null });
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public async Task Un_sitio_web_vacio_en_un_proveedor_sin_sitio_lo_deja_nulo(string json)
    {
        var id = await SeedAsync(website: null);
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchRawAsync(id, $$"""{"website":{{json}},"contactPhone":"{{NewPhone}}"}""");

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await AssertOnlyChangedAsync(before, id, before.Single() with { ContactPhone = NewPhone, Website = null });
    }

    [Theory]
    [MemberData(nameof(MissingRequiredFields))]
    public async Task Un_obligatorio_nulo_vacio_o_en_blanco_responde_400_y_no_cambia_ni_el_campo_valido_enviado_junto(
        string field, string? value, string code, string title)
    {
        await SeedAsync(isActive: false);
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchAsync(before.Single().Id, WithValidCompanion(field, value));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, code, title);
        await AssertUnchangedAsync(before);
    }

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public async Task Longitudes_excedidas_correos_y_sitios_invalidos_responden_400_y_no_cambian_nada(
        string field, string value, string code, string title)
    {
        await SeedAsync();
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchAsync(before.Single().Id, WithValidCompanion(field, value));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, code, title);
        await AssertUnchangedAsync(before);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"isActive\":false}")]
    [InlineData("{\"id\":99}")]
    [InlineData("{\"isActive\":false,\"id\":99,\"createdAt\":null}")]
    public async Task Un_cuerpo_sin_campos_conocidos_responde_400_NoFieldsProvided_y_no_cambia_ni_el_estado_ni_el_id(string json)
    {
        var id = await SeedAsync();
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchRawAsync(id, json);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, InsuranceProviderErrors.NoFieldsProvided.Code,
            InsuranceProviderErrors.NoFieldsProvided.Description);
        await AssertUnchangedAsync(before);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task Un_cuerpo_vacio_o_JSON_null_responde_400_y_no_cambia_nada(string json)
    {
        var id = await SeedAsync();
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchRawAsync(id, json);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        await AssertUnchangedAsync(before);
    }

    [Theory]
    [MemberData(nameof(ErrorPriority))]
    public async Task Con_varios_campos_invalidos_responde_el_error_del_primero_en_el_orden_del_dominio(
        bool badName, bool badPhone, bool badEmail, bool badAddress, bool badWebsite, string code)
    {
        var id = await SeedAsync();
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchAsync(id, new Dictionary<string, string?>
        {
            ["name"] = badName ? "" : OtherName,
            ["contactPhone"] = badPhone ? "  " : NewPhone,
            ["contactEmail"] = badEmail ? "sin-arroba" : "nuevo@seguros.com",
            ["address"] = badAddress ? "" : "Calle Nueva 5",
            ["website"] = badWebsite ? "ftp://x.com" : "https://nuevo.com"
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(code);
        await AssertUnchangedAsync(before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Un_id_no_positivo_responde_400_InvalidId_sin_modificar_datos(int id)
    {
        await SeedAsync();
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchAsync(id, new Dictionary<string, string?> { ["name"] = OtherName });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, InsuranceProviderErrors.InvalidId.Code,
            InsuranceProviderErrors.InvalidId.Description);
        await AssertUnchangedAsync(before);
    }

    [Fact]
    public async Task Un_proveedor_inexistente_responde_404_sin_crear_ni_modificar_datos()
    {
        await SeedAsync();
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchAsync(int.MaxValue, new Dictionary<string, string?>
        {
            ["name"] = OtherName, ["contactPhone"] = NewPhone
        });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, InsuranceProviderErrors.NotFound.Code,
            InsuranceProviderErrors.NotFound.Description);
        await AssertUnchangedAsync(before);
    }

    [Fact]
    public async Task Un_proveedor_inexistente_con_campos_invalidos_responde_400_porque_la_validacion_va_antes_que_la_busqueda()
    {
        await SeedAsync();
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        // El decorador de validación corre antes del handler: el 404 solo aparece si el cuerpo es válido.
        using var response = await PatchAsync(int.MaxValue, new Dictionary<string, string?>
        {
            ["name"] = "", ["contactPhone"] = NewPhone
        });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, InsuranceProviderErrors.NameRequired.Code,
            InsuranceProviderErrors.NameRequired.Description);
        await AssertUnchangedAsync(before);
    }

    [Theory]
    [InlineData(true, "Seguros Nacional")]
    [InlineData(false, "Seguros Nacional")]
    [InlineData(true, "  Seguros Nacional  ")]
    [InlineData(false, "  Seguros Nacional  ")]
    public async Task El_nombre_de_otro_proveedor_activo_o_inactivo_responde_409_y_deja_ambos_intactos(
        bool otherIsActive, string requestedName)
    {
        var id = await SeedAsync();
        await SeedAsync(OtherName, otherIsActive);
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchAsync(id, new Dictionary<string, string?>
        {
            ["name"] = requestedName, ["contactPhone"] = NewPhone
        });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, InsuranceProviderErrors.NameAlreadyInUse.Code,
            InsuranceProviderErrors.NameAlreadyInUse.Description);
        await AssertUnchangedAsync(before);
    }

    [Fact]
    public async Task Un_nombre_que_solo_cambia_la_capitalizacion_de_otro_proveedor_se_acepta()
    {
        var id = await SeedAsync();
        await SeedAsync(OtherName);
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        // Igual que en el registro: uq_insurance_provider_name distingue mayúsculas y tildes.
        using var response = await PatchAsync(id, new Dictionary<string, string?> { ["name"] = OtherName.ToUpperInvariant() });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await AssertOnlyChangedAsync(before, id, before.Single(item => item.Id == id) with { Name = OtherName.ToUpperInvariant() });
    }

    [Theory]
    [InlineData("Seguros Universal")]
    [InlineData("  Seguros Universal  ")]
    public async Task Enviar_el_propio_nombre_es_204_y_excluye_el_id_propio_de_la_comprobacion(string name)
    {
        var id = await SeedAsync();
        await SeedAsync(OtherName);
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        using var response = await PatchAsync(id, new Dictionary<string, string?> { ["name"] = name });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await AssertSameValuesAsync(before);
    }

    [Fact]
    public async Task Enviar_exactamente_los_mismos_valores_es_204_sin_cambios()
    {
        var id = await SeedAsync(isActive: false);
        var before = await ReadAllAsync();
        var current = before.Single();
        Authorize(UserRole.Administrator);

        using var response = await PatchAsync(id, new Dictionary<string, string?>
        {
            ["name"] = current.Name, ["contactPhone"] = current.ContactPhone, ["contactEmail"] = current.ContactEmail,
            ["address"] = current.Address, ["website"] = current.Website
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await AssertSameValuesAsync(before);
    }

    [Fact]
    public async Task Un_nombre_libre_cambia_aunque_exista_otro_proveedor_con_un_nombre_parecido()
    {
        var id = await SeedAsync();
        await SeedAsync("Seguros Nacional S.A.");
        var before = await ReadAllAsync();
        Authorize(UserRole.Administrator);

        // Control de que la comprobación es por nombre exacto y no por prefijo ni por coincidencia parcial.
        using var response = await PatchAsync(id, new Dictionary<string, string?> { ["name"] = OtherName });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await AssertOnlyChangedAsync(before, id, before.Single(item => item.Id == id) with { Name = OtherName });
    }

    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Doctor)]
    public async Task El_JWT_real_de_otro_rol_responde_403_sin_modificar_datos(UserRole role)
    {
        var id = await SeedAsync();
        var before = await ReadAllAsync();
        Authorize(role);

        using var response = await PatchAsync(id, new Dictionary<string, string?> { ["name"] = OtherName });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await AssertUnchangedAsync(before);
    }

    [Theory]
    [InlineData("anonimo")]
    [InlineData("invalido")]
    [InlineData("vencido")]
    public async Task Sin_un_JWT_valido_responde_401_sin_modificar_datos(string authentication)
    {
        var id = await SeedAsync();
        var before = await ReadAllAsync();
        if (authentication == "invalido")
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");
        if (authentication == "vencido")
        {
            Clock.UtcNow = DateTimeOffset.UtcNow.AddHours(-1);
            SetToken(UserRole.Administrator, Client);
        }

        using var response = await PatchAsync(id, new Dictionary<string, string?> { ["name"] = OtherName });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        await AssertUnchangedAsync(before);
    }

    [Fact]
    public async Task Un_conflicto_del_indice_PostgreSQL_durante_el_UPDATE_responde_409_y_revierte_los_datos()
    {
        var id = await SeedAsync(isActive: false);
        await SeedAsync(OtherName);
        var before = await ReadAllAsync();
        var checks = 0;
        var failedUpdate = new FailedUpdateObserver();
        var transactions = new TransactionObserver();
        await using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.AddScoped(provider => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(
                provider.GetRequiredService<DbContextOptions<AppDbContext>>())
                .AddInterceptors(failedUpdate, transactions).Options));
            services.RemoveAll<IInsuranceProviderRepository>();
            services.AddScoped<IInsuranceProviderRepository>(provider => new SkipDuplicateCheckRepository(
                new InsuranceProviderRepository(provider.GetRequiredService<AppDbContext>()),
                () => Interlocked.Increment(ref checks)));
        }));
        using var client = app.CreateClient();
        Authorize(UserRole.Administrator, client);

        using var response = await client.PatchAsJsonAsync(Url(id), new Dictionary<string, string?>
        {
            ["name"] = OtherName, ["contactPhone"] = NewPhone
        });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, InsuranceProviderErrors.NameAlreadyInUse.Code,
            InsuranceProviderErrors.NameAlreadyInUse.Description);
        checks.ShouldBe(1);
        failedUpdate.CommandText.ShouldStartWith("UPDATE people.insurance_provider");
        var postgres = failedUpdate.Exception.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.SchemaName.ShouldBe("people");
        postgres.TableName.ShouldBe("insurance_provider");
        postgres.ConstraintName.ShouldBe("uq_insurance_provider_name");
        transactions.Rollbacks.ShouldBe(1);
        transactions.Commits.ShouldBe(0);
        await AssertUnchangedAsync(before);
    }

    private static string Url(int id) => $"/api/insurance-providers/{id}";

    private static string Padded(string value) => $"  {value}  ";

    private static string WebsiteOfLength(int length) => WebsitePrefix + new string('c', length - WebsitePrefix.Length);

    // El cuerpo lleva siempre un segundo campo válido y distinto para demostrar que el rechazo es atómico.
    private static Dictionary<string, string?> WithValidCompanion(string field, string? value) => new()
    {
        [field] = value,
        [field == "name" ? "contactPhone" : "name"] = field == "name" ? NewPhone : OtherName
    };

    // Valor enviado con espacios y mayúsculas, y cómo debe quedar guardado.
    private static (string Sent, string Normalized) ChangeOf(string field) => field switch
    {
        "name" => ("  Seguros Nacional  ", "Seguros Nacional"),
        "contactPhone" => ("  809-555-0999  ", "809-555-0999"),
        "contactEmail" => ("  NUEVO@Seguros.COM  ", "nuevo@seguros.com"),
        "address" => ("  Calle Nueva 5, Santiago  ", "Calle Nueva 5, Santiago"),
        "website" => ("  https://nuevo.com/inicio  ", "https://nuevo.com/inicio"),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Campo no previsto en la prueba.")
    };

    private static Snapshot Apply(Snapshot original, string field, string value) => field switch
    {
        "name" => original with { Name = value },
        "contactPhone" => original with { ContactPhone = value },
        "contactEmail" => original with { ContactEmail = value },
        "address" => original with { Address = value },
        "website" => original with { Website = value },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Campo no previsto en la prueba.")
    };

    private void Authorize(UserRole role, HttpClient? client = null)
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        SetToken(role, client ?? Client);
    }

    private void SetToken(UserRole role, HttpClient client)
    {
        var token = Factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(1, role, 1, null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }

    private Task<HttpResponseMessage> PatchAsync(int id, Dictionary<string, string?> body) =>
        Client.PatchAsJsonAsync(Url(id), body);

    private Task<HttpResponseMessage> PatchRawAsync(int id, string json) =>
        Client.PatchAsync(Url(id), new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<int> SeedAsync(string? name = null, bool isActive = true, string? website = InsuranceProviderMother.DefaultWebsite)
    {
        var provider = InsuranceProviderMother.Valid(name, website);
        if (!isActive) provider.Deactivate().IsSuccess.ShouldBeTrue();
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInsuranceProviderRepository>().Add(provider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return provider.Id;
    }

    // Un scope nuevo y AsNoTracking evitan que la caché del contexto oculte fallos de persistencia.
    private async Task<Snapshot[]> ReadAllAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().InsuranceProviders
            .AsNoTracking().OrderBy(value => value.Id).ToListAsync();
        return rows.Select(value => new Snapshot(value.Id, value.Name, value.ContactPhone, value.ContactEmail.Value,
            value.Address, value.Website, value.IsActive)).ToArray();
    }

    // Ids con updated_at: el trigger de auditoría solo lo llena cuando hay un UPDATE real.
    private async Task<int[]> TouchedIdsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Database.SqlQuery<int>(
            $"""SELECT id AS "Value" FROM people.insurance_provider WHERE updated_at IS NOT NULL ORDER BY id""").ToArrayAsync();
    }

    private async Task AssertOnlyChangedAsync(Snapshot[] before, int id, Snapshot expected)
    {
        var after = await ReadAllAsync();
        after.Select(value => value.Id).ShouldBe(before.Select(value => value.Id));
        foreach (var row in after)
            row.ShouldBe(row.Id == id ? expected : before.Single(value => value.Id == row.Id));
    }

    private async Task AssertSameValuesAsync(Snapshot[] before) => (await ReadAllAsync()).ShouldBe(before);

    private async Task AssertUnchangedAsync(Snapshot[] before)
    {
        await AssertSameValuesAsync(before);
        (await TouchedIdsAsync()).ShouldBeEmpty();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code, string title)
    {
        await response.ShouldHaveStatusAsync(status);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe((int)status);
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("title").GetString().ShouldBe(title);
    }

    private sealed record Snapshot(
        int Id, string Name, string ContactPhone, string ContactEmail, string Address, string? Website, bool IsActive)
    {
        public Snapshot WithTexts(string name, string phone, string address, string website) =>
            this with { Name = name, ContactPhone = phone, Address = address, Website = website };
    }

    // Observan eventos posteriores a las operaciones reales; no suprimen comandos ni errores.
    private sealed class FailedUpdateObserver : DbCommandInterceptor
    {
        public string CommandText { get; private set; } = string.Empty;
        public Exception? Exception { get; private set; }

        public override Task CommandFailedAsync(
            DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            CommandText = command.CommandText;
            Exception = eventData.Exception;
            return Task.CompletedTask;
        }
    }

    private sealed class TransactionObserver : DbTransactionInterceptor
    {
        public int Rollbacks { get; private set; }
        public int Commits { get; private set; }

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Rollbacks++;
            return Task.CompletedTask;
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Commits++;
            return Task.CompletedTask;
        }
    }

    // Omite únicamente la consulta previa; el UPDATE, la transacción y el índice son reales.
    private sealed class SkipDuplicateCheckRepository(IInsuranceProviderRepository inner, Action onCheck)
        : IInsuranceProviderRepository
    {
        public Task<InsuranceProvider?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            inner.GetByIdAsync(id, cancellationToken);

        public Task<bool> ExistsByNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onCheck();
            return Task.FromResult(false);
        }

        public void Add(InsuranceProvider provider) => inner.Add(provider);
    }
}
