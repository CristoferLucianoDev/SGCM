using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class RegisterInsuranceProviderTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Url = "/api/insurance-providers";
    private const string EmailDomain = "@correo.com";

    // El relleno del sitio web va en la ruta para no depender de cómo Uri trata un host muy largo.
    private const string WebsitePrefix = "https://seguros.com/";

    public static TheoryData<string, string?, string, string> MissingRequiredFields => new()
    {
        { nameof(RegisterInsuranceProviderRequest.Name), null, InsuranceProviderErrors.NameRequired.Code, InsuranceProviderErrors.NameRequired.Description },
        { nameof(RegisterInsuranceProviderRequest.Name), "", InsuranceProviderErrors.NameRequired.Code, InsuranceProviderErrors.NameRequired.Description },
        { nameof(RegisterInsuranceProviderRequest.Name), "   ", InsuranceProviderErrors.NameRequired.Code, InsuranceProviderErrors.NameRequired.Description },
        { nameof(RegisterInsuranceProviderRequest.ContactPhone), null, InsuranceProviderErrors.ContactPhoneRequired.Code, InsuranceProviderErrors.ContactPhoneRequired.Description },
        { nameof(RegisterInsuranceProviderRequest.ContactPhone), "", InsuranceProviderErrors.ContactPhoneRequired.Code, InsuranceProviderErrors.ContactPhoneRequired.Description },
        { nameof(RegisterInsuranceProviderRequest.ContactPhone), "   ", InsuranceProviderErrors.ContactPhoneRequired.Code, InsuranceProviderErrors.ContactPhoneRequired.Description },
        { nameof(RegisterInsuranceProviderRequest.Address), null, InsuranceProviderErrors.AddressRequired.Code, InsuranceProviderErrors.AddressRequired.Description },
        { nameof(RegisterInsuranceProviderRequest.Address), "", InsuranceProviderErrors.AddressRequired.Code, InsuranceProviderErrors.AddressRequired.Description },
        { nameof(RegisterInsuranceProviderRequest.Address), "   ", InsuranceProviderErrors.AddressRequired.Code, InsuranceProviderErrors.AddressRequired.Description }
    };

    public static TheoryData<string?> InvalidEmails => new()
    {
        null,
        "",
        "   ",
        "sin-arroba",
        new string('a', Email.MaxLength + 1 - EmailDomain.Length) + EmailDomain
    };

    public static TheoryData<string, string, string, string> TooLongFields => new()
    {
        { nameof(RegisterInsuranceProviderRequest.Name), new string('a', InsuranceProvider.MaxNameLength + 1),
            InsuranceProviderErrors.NameTooLong.Code, InsuranceProviderErrors.NameTooLong.Description },
        { nameof(RegisterInsuranceProviderRequest.ContactPhone), new string('1', InsuranceProvider.MaxContactPhoneLength + 1),
            InsuranceProviderErrors.ContactPhoneTooLong.Code, InsuranceProviderErrors.ContactPhoneTooLong.Description },
        { nameof(RegisterInsuranceProviderRequest.Address), new string('b', InsuranceProvider.MaxAddressLength + 1),
            InsuranceProviderErrors.AddressTooLong.Code, InsuranceProviderErrors.AddressTooLong.Description },
        { nameof(RegisterInsuranceProviderRequest.Website), WebsiteOfLength(InsuranceProvider.MaxWebsiteLength + 1),
            InsuranceProviderErrors.WebsiteTooLong.Code, InsuranceProviderErrors.WebsiteTooLong.Description }
    };

    [Fact]
    public async Task Administrator_registra_un_proveedor_activo_con_sus_datos_recortados()
    {
        Authorize(UserRole.Administrator);
        const string email = "  Contacto@Seguros.COM  ";

        using var response = await PostAsync(new RegisterInsuranceProviderRequest(
            "  Seguros Universal  ", "  8095550200  ", email, "  Av. Winston Churchill 100  ",
            "  https://www.segurosuniversal.com/planes  "));

        var id = await ReadCreatedIdAsync(response);
        var saved = await FindAsync(id);
        saved.Name.ShouldBe("Seguros Universal");
        saved.ContactPhone.ShouldBe("8095550200");
        saved.ContactEmail.ShouldBe(Email.Create(email).Value);
        saved.Address.ShouldBe("Av. Winston Churchill 100");
        saved.Website.ShouldBe("https://www.segurosuniversal.com/planes");
        saved.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Administrator_puede_registrar_con_las_longitudes_maximas()
    {
        Authorize(UserRole.Administrator);
        var name = new string('a', InsuranceProvider.MaxNameLength);
        var phone = new string('1', InsuranceProvider.MaxContactPhoneLength);
        var address = new string('b', InsuranceProvider.MaxAddressLength);
        var website = WebsiteOfLength(InsuranceProvider.MaxWebsiteLength);

        using var response = await PostAsync(new RegisterInsuranceProviderRequest(
            Padded(name), Padded(phone), InsuranceProviderMother.DefaultContactEmail, Padded(address), Padded(website)));

        var saved = await FindAsync(await ReadCreatedIdAsync(response));
        saved.Name.ShouldBe(name);
        saved.ContactPhone.ShouldBe(phone);
        saved.Address.ShouldBe(address);
        saved.Website.ShouldBe(website);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Sin_sitio_web_se_guarda_sin_sitio(string? website)
    {
        Authorize(UserRole.Administrator);

        using var response = await PostAsync(ValidRequest() with { Website = website });

        (await FindAsync(await ReadCreatedIdAsync(response))).Website.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(MissingRequiredFields))]
    public async Task Datos_obligatorios_ausentes_responden_400_con_el_error_del_campo(
        string field, string? value, string errorCode, string title)
    {
        Authorize(UserRole.Administrator);
        var idsBeforeRequest = await ReadIdsAsync();

        using var response = await PostAsync(WithField(field, value));

        await AssertValidationErrorAsync(response, errorCode, title, idsBeforeRequest);
    }

    [Theory]
    [MemberData(nameof(InvalidEmails))]
    public async Task Correo_invalido_responde_400_con_el_error_del_correo(string? contactEmail)
    {
        Authorize(UserRole.Administrator);
        var idsBeforeRequest = await ReadIdsAsync();

        using var response = await PostAsync(ValidRequest() with { ContactEmail = contactEmail! });

        await AssertValidationErrorAsync(response, CommonErrors.InvalidEmail.Code, CommonErrors.InvalidEmail.Description, idsBeforeRequest);
    }

    [Theory]
    [MemberData(nameof(TooLongFields))]
    public async Task Longitudes_excedidas_responden_400(string field, string value, string errorCode, string title)
    {
        Authorize(UserRole.Administrator);
        var idsBeforeRequest = await ReadIdsAsync();

        using var response = await PostAsync(WithField(field, value));

        await AssertValidationErrorAsync(response, errorCode, title, idsBeforeRequest);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://seguros.com")]
    [InlineData("www.seguros.com")]
    public async Task Sitio_web_que_no_es_una_url_http_responde_400(string website)
    {
        Authorize(UserRole.Administrator);
        var idsBeforeRequest = await ReadIdsAsync();

        using var response = await PostAsync(ValidRequest() with { Website = website });

        await AssertValidationErrorAsync(response, InsuranceProviderErrors.WebsiteInvalid.Code,
            InsuranceProviderErrors.WebsiteInvalid.Description, idsBeforeRequest);
    }

    [Fact]
    public async Task Con_varios_campos_invalidos_responde_el_error_del_primero()
    {
        Authorize(UserRole.Administrator);
        var idsBeforeRequest = await ReadIdsAsync();

        using var response = await PostAsync(ValidRequest() with { Name = "", ContactEmail = "sin-arroba" });

        await AssertValidationErrorAsync(response, InsuranceProviderErrors.NameRequired.Code,
            InsuranceProviderErrors.NameRequired.Description, idsBeforeRequest);
    }

    [Fact]
    public async Task Sin_token_responde_401_sin_registrar_nada()
    {
        using var response = await PostAsync(ValidRequest());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await ReadIdsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_invalido_responde_401_sin_registrar_nada()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");

        using var response = await PostAsync(ValidRequest());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await ReadIdsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_Administrator_vencido_responde_401_sin_registrar_nada()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow.AddHours(-1);
        var token = Factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(1, UserRole.Administrator, 1, null);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        using var response = await PostAsync(ValidRequest());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await ReadIdsAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Doctor)]
    public async Task Otros_roles_responden_403_sin_registrar_nada(UserRole role)
    {
        Authorize(role);

        using var response = await PostAsync(ValidRequest());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await ReadIdsAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(InsuranceProviderMother.DefaultName, true)]
    [InlineData(InsuranceProviderMother.DefaultName, false)]
    [InlineData("  " + InsuranceProviderMother.DefaultName + "  ", true)]
    public async Task Nombre_existente_responde_409_incluso_si_esta_inactivo(string name, bool isActive)
    {
        Authorize(UserRole.Administrator);
        var existing = InsuranceProviderMother.Valid();
        if (!isActive)
        {
            existing.Deactivate().IsSuccess.ShouldBeTrue();
        }

        await SeedAsync(existing);

        using var response = await PostAsync(ValidRequest() with
        {
            Name = name,
            ContactPhone = "8295550300",
            Address = "Otra dirección 200"
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(InsuranceProviderErrors.NameAlreadyInUse.Code);
        (await ReadIdsAsync()).ShouldBe(new[] { existing.Id });
        var saved = await FindAsync(existing.Id);
        saved.ContactPhone.ShouldBe(InsuranceProviderMother.DefaultContactPhone);
        saved.Address.ShouldBe(InsuranceProviderMother.DefaultAddress);
        saved.IsActive.ShouldBe(isActive);
    }

    [Fact]
    public async Task Un_nombre_con_distinta_capitalizacion_se_registra()
    {
        // Documenta la regla exacta de uq_insurance_provider_name decidida en AB#238: distingue
        // mayúsculas y tildes, a diferencia de las especialidades.
        Authorize(UserRole.Administrator);
        await SeedAsync(InsuranceProviderMother.Valid());

        using var response = await PostAsync(ValidRequest() with { Name = InsuranceProviderMother.DefaultName.ToUpperInvariant() });

        await ReadCreatedIdAsync(response);
        (await ReadIdsAsync()).Length.ShouldBe(2);
    }

    [Fact]
    public async Task Conflicto_en_PostgreSQL_durante_el_guardado_llega_como_409()
    {
        await SeedAsync(InsuranceProviderMother.Valid());
        var checks = 0;
        await using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInsuranceProviderRepository>();
            services.AddScoped<IInsuranceProviderRepository>(provider => new SkipDuplicateCheckRepository(
                new InsuranceProviderRepository(provider.GetRequiredService<AppDbContext>()), () => Interlocked.Increment(ref checks)));
        }));
        using var client = app.CreateClient();
        Authorize(UserRole.Administrator, client);

        using var response = await PostAsync(ValidRequest(), client);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(InsuranceProviderErrors.NameAlreadyInUse.Code);
        checks.ShouldBe(1);
        (await CountByNameAsync(InsuranceProviderMother.DefaultName)).ShouldBe(1);
    }

    private static RegisterInsuranceProviderRequest ValidRequest() =>
        new(InsuranceProviderMother.DefaultName, InsuranceProviderMother.DefaultContactPhone,
            InsuranceProviderMother.DefaultContactEmail, InsuranceProviderMother.DefaultAddress,
            InsuranceProviderMother.DefaultWebsite);

    private static RegisterInsuranceProviderRequest WithField(string field, string? value) => field switch
    {
        nameof(RegisterInsuranceProviderRequest.Name) => ValidRequest() with { Name = value! },
        nameof(RegisterInsuranceProviderRequest.ContactPhone) => ValidRequest() with { ContactPhone = value! },
        nameof(RegisterInsuranceProviderRequest.ContactEmail) => ValidRequest() with { ContactEmail = value! },
        nameof(RegisterInsuranceProviderRequest.Address) => ValidRequest() with { Address = value! },
        nameof(RegisterInsuranceProviderRequest.Website) => ValidRequest() with { Website = value },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Campo no previsto en la prueba.")
    };

    private static string WebsiteOfLength(int length) => WebsitePrefix + new string('c', length - WebsitePrefix.Length);

    private static string Padded(string value) => $"  {value}  ";

    private static async Task<int> ReadCreatedIdAsync(HttpResponseMessage response)
    {
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<RegisterInsuranceProviderResponse>())!;
        created.Id.ShouldBeGreaterThan(0);
        return created.Id;
    }

    private async Task AssertValidationErrorAsync(HttpResponseMessage response, string code, string title, int[] idsBeforeRequest)
    {
        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe(400);
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("title").GetString().ShouldBe(title);
        (await ReadIdsAsync()).ShouldBe(idsBeforeRequest);
    }

    private async Task<int[]> ReadIdsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InsuranceProviders
            .OrderBy(value => value.Id).Select(value => value.Id).ToArrayAsync();
    }

    private async Task<InsuranceProvider> FindAsync(int id)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InsuranceProviders
            .AsNoTracking().SingleAsync(value => value.Id == id);
    }

    private async Task<int> CountByNameAsync(string name)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InsuranceProviders
            .CountAsync(value => value.Name == name);
    }

    private void Authorize(UserRole role, HttpClient? client = null)
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var token = Factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(1, role, 1, null);
        (client ?? Client).DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }

    private Task<HttpResponseMessage> PostAsync(RegisterInsuranceProviderRequest request, HttpClient? client = null) =>
        (client ?? Client).PostAsJsonAsync(Url, request);

    private async Task SeedAsync(InsuranceProvider provider)
    {
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInsuranceProviderRepository>().Add(provider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    // Simula la ventana de concurrencia sin reemplazar el INSERT ni la restricción de PostgreSQL.
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
