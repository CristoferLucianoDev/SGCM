using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class RegistroDeProveedoresDeSegurosSteps(ApiScenario scenario)
{
    private const string Url = "/api/insurance-providers";
    private const string InvalidEmail = "sin-arroba";
    private const string InvalidWebsite = "javascript:alert(1)";

    // El relleno del sitio web va en la ruta para no depender de cómo Uri trata un host muy largo.
    private const string WebsitePrefix = "https://seguros.com/";

    private int _countBeforeRequest;
    private RegisterInsuranceProviderRequest? _lastRequest;
    private int _createdId;

    [Given("que estoy autenticado para gestionar proveedores de seguros como {string}")]
    public Task DadoUnUsuarioAutenticado(string role) =>
        AuthenticatedScenario.LoginAsync(scenario, role, "proveedores");

    [Given("que no estoy autenticado para gestionar proveedores de seguros")]
    public void DadoUnUsuarioAnonimo() => scenario.Client.DefaultRequestHeaders.Authorization = null;

    [When("registro el proveedor de seguros {string} con todos sus datos")]
    public Task RegistroConTodosLosDatos(string name) => PostAsync(ValidRequest() with { Name = name });

    [When("registro el proveedor de seguros {string} sin sitio web")]
    public Task RegistroSinSitioWeb(string name) => PostAsync(ValidRequest() with { Name = name, Website = null });

    [When("registro un proveedor de seguros con el campo {string} vacío")]
    public Task RegistroConCampoVacio(string field) => PostAsync(WithField(field, string.Empty));

    [When("registro un proveedor de seguros con el campo {string} ausente")]
    public Task RegistroConCampoAusente(string field) => PostAsync(WithField(field, null));

    [When("registro un proveedor de seguros con el campo {string} que supera su longitud")]
    public Task RegistroConCampoDemasiadoLargo(string field) => PostAsync(WithField(field, TooLongValue(field)));

    [When("registro un proveedor de seguros con el campo {string} con formato inválido")]
    public Task RegistroConFormatoInvalido(string field) => PostAsync(WithField(field, field switch
    {
        nameof(RegisterInsuranceProviderRequest.ContactEmail) => InvalidEmail,
        nameof(RegisterInsuranceProviderRequest.Website) => InvalidWebsite,
        _ => throw new InvalidOperationException($"El campo {field} no tiene un formato que validar.")
    }));

    [Then("la API de proveedores de seguros responde HTTP {int}")]
    public Task LaApiResponde(int status) => Response.ShouldHaveStatusAsync((HttpStatusCode)status);

    [Then("el error del proveedor de seguros tiene el código {string} y el mensaje {string}")]
    public async Task ElErrorIdentificaElDato(string code, string title)
    {
        var body = await Response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe((int)Response.StatusCode);
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("title").GetString().ShouldBe(title);
    }

    [Then("recibo el ID real del proveedor de seguros registrado")]
    public async Task ReciboElIdReal()
    {
        var provider = await FindCreatedAsync();
        provider.Name.ShouldBe(_lastRequest!.Name);
    }

    [Then("el proveedor de seguros registrado está activo con los datos enviados")]
    public async Task ElProveedorEstaActivoConLosDatosEnviados()
    {
        var sent = _lastRequest ?? throw new InvalidOperationException("No se envió ningún registro.");
        var provider = await FindCreatedAsync();

        provider.Name.ShouldBe(sent.Name);
        provider.ContactPhone.ShouldBe(sent.ContactPhone);
        provider.ContactEmail.ShouldBe(Email.Create(sent.ContactEmail).Value);
        provider.Address.ShouldBe(sent.Address);
        provider.Website.ShouldBe(sent.Website);
        provider.IsActive.ShouldBeTrue();
    }

    [Then("el proveedor de seguros registrado no tiene sitio web")]
    public async Task ElProveedorNoTieneSitioWeb() => (await FindCreatedAsync()).Website.ShouldBeNull();

    [Then("no se agregó ningún proveedor de seguros")]
    public async Task NoSeAgregoNingunProveedor() => (await CountAsync()).ShouldBe(_countBeforeRequest);

    private HttpResponseMessage Response =>
        scenario.Response ?? throw new InvalidOperationException("Todavía no se envió ninguna petición.");

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
        _ => throw new InvalidOperationException($"El campo {field} no existe en el registro del proveedor.")
    };

    private static string TooLongValue(string field) => field switch
    {
        nameof(RegisterInsuranceProviderRequest.Name) => new string('a', InsuranceProvider.MaxNameLength + 1),
        nameof(RegisterInsuranceProviderRequest.ContactPhone) => new string('1', InsuranceProvider.MaxContactPhoneLength + 1),
        nameof(RegisterInsuranceProviderRequest.Address) => new string('b', InsuranceProvider.MaxAddressLength + 1),
        nameof(RegisterInsuranceProviderRequest.Website) =>
            WebsitePrefix + new string('c', InsuranceProvider.MaxWebsiteLength + 1 - WebsitePrefix.Length),
        _ => throw new InvalidOperationException($"El campo {field} no tiene una longitud máxima que probar.")
    };

    private async Task PostAsync(RegisterInsuranceProviderRequest request)
    {
        _countBeforeRequest = await CountAsync();
        _lastRequest = request;
        _createdId = 0;
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.PostAsJsonAsync(Url, request);
    }

    private async Task ReadCreatedIdAsync()
    {
        if (_createdId > 0)
        {
            return;
        }

        await Response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var created = await Response.Content.ReadFromJsonAsync<RegisterInsuranceProviderResponse>();
        created.ShouldNotBeNull();
        created.Id.ShouldBeGreaterThan(0);
        _createdId = created.Id;
    }

    private async Task<int> CountAsync()
    {
        using var scope = scenario.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InsuranceProviders.CountAsync();
    }

    private async Task<InsuranceProvider> FindCreatedAsync()
    {
        await ReadCreatedIdAsync();

        using var scope = scenario.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InsuranceProviders
            .AsNoTracking().SingleAsync(value => value.Id == _createdId);
    }
}
