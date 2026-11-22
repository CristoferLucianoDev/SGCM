using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.People.InsuranceProviders;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

// No necesita un [AfterScenario] de limpieza: los escenarios son @Integration, así que el hook
// BeforeScenario reinicia la base (reset.sql borra people.insurance_provider) antes de cada uno.
[Binding]
public sealed class VisualizacionDeProveedoresDeSegurosSteps(ApiScenario scenario)
{
    private const string Url = "/api/insurance-providers";

    private readonly List<InsuranceProvider> _created = [];
    private JsonElement _body;

    [Given("que no estoy autenticado para consultar proveedores de seguros")]
    public void DadoUnUsuarioAnonimo() => scenario.Client.DefaultRequestHeaders.Authorization = null;

    [Given("que estoy autenticado para consultar proveedores de seguros como {string}")]
    public Task DadoUnUsuarioAutenticado(string role) =>
        AuthenticatedScenario.LoginAsync(scenario, role, "listado-proveedores");

    [Given("que el catálogo de proveedores de seguros contiene:")]
    public async Task DadoElCatalogo(Table table)
    {
        using var scope = scenario.Factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInsuranceProviderRepository>();
        foreach (var row in table.Rows)
        {
            var website = row["Sitio web"];
            var provider = InsuranceProvider.Create(
                row["Nombre"], row["Teléfono"], row["Correo"], row["Dirección"],
                string.IsNullOrWhiteSpace(website) ? null : website).Value;
            switch (row["Estado"])
            {
                case "Activo": break;
                case "Inactivo": provider.Deactivate().IsSuccess.ShouldBeTrue(); break;
                default: throw new ArgumentOutOfRangeException(nameof(table), row["Estado"], "Estado de prueba desconocido.");
            }
            _created.Add(provider);
            repository.Add(provider);
        }
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        _created.ShouldAllBe(provider => provider.Id > 0);
    }

    [When("consulto la página {int} de tamaño {int} de proveedores de seguros con estado {string}")]
    public Task ConsultoLaPagina(int page, int size, string status) => GetAsync(new Dictionary<string, string?>
    {
        ["pageNumber"] = page.ToString(CultureInfo.InvariantCulture),
        ["pageSize"] = size.ToString(CultureInfo.InvariantCulture),
        ["status"] = status
    });

    [When("busco proveedores de seguros por {string}")]
    public Task BuscoProveedores(string search) => GetAsync(new Dictionary<string, string?> { ["search"] = search });

    [When("consulto los proveedores de seguros con estado {string}")]
    public Task ConsultoElEstado(string status) => GetAsync(new Dictionary<string, string?> { ["status"] = status });

    [Then("recibo el proveedor de seguros {string} con todos sus datos")]
    public void ReciboElProveedorConTodosSusDatos(string name)
    {
        var expected = FindCreated(name);
        expected.Website.ShouldNotBeNull();
        AssertItems([expected], false);
    }

    [Then("recibo el proveedor de seguros {string} sin sitio web")]
    public void ReciboElProveedorSinSitioWeb(string name)
    {
        var expected = FindCreated(name);
        expected.Website.ShouldBeNull();
        AssertItems([expected], false);
        var item = _body.GetProperty("items").EnumerateArray().Single();
        item.TryGetProperty("website", out var website).ShouldBeTrue();
        website.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Then("la página de proveedores recibida es {int} y su tamaño es {int}")]
    public void LaPaginaEs(int page, int size)
    {
        _body.GetProperty("pageNumber").GetInt32().ShouldBe(page);
        _body.GetProperty("pageSize").GetInt32().ShouldBe(size);
    }

    [Then("el total de proveedores de seguros disponibles es {int}")]
    public void ElTotalEs(int total) => _body.GetProperty("totalCount").GetInt32().ShouldBe(total);

    [Then("no se expone el estado de los proveedores de seguros")]
    public void NoSeExponeElEstado()
    {
        var items = _body.GetProperty("items").EnumerateArray().ToArray();
        items.ShouldNotBeEmpty();
        foreach (var item in items) item.TryGetProperty("isActive", out _).ShouldBeFalse();
    }

    [Then("recibo únicamente el proveedor de seguros {string}")]
    public void ReciboSoloElProveedor(string name)
    {
        AssertItems([FindCreated(name)], false);
        ElTotalEs(1);
    }

    [Then("recibo {int} proveedores de seguros correspondientes al filtro {string}")]
    public void ReciboElFiltro(int count, string status)
    {
        bool? active = status switch
        {
            "Active" => true,
            "Inactive" => false,
            "All" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
        // El orden se calcula con Ordinal, pero la base ordena con su collation: los nombres de prueba usados para
        // ordenar difieren ya en la primera letra tras "Seguros " (M, N, U), que es ASCII, así que ambos criterios coinciden.
        var expected = _created.Where(value => !active.HasValue || value.IsActive == active.Value)
            .OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
        expected.Length.ShouldBe(count);
        AssertItems(expected, true);
        ElTotalEs(count);
    }

    [Then("recibo únicamente los {int} proveedores de seguros activos")]
    public void ReciboSoloActivos(int count)
    {
        // Mismo criterio de orden que en el filtro por estado: el desempate lo decide una letra ASCII (M frente a U).
        var expected = _created.Where(value => value.IsActive).OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
        expected.Length.ShouldBe(count);
        AssertItems(expected, false);
        ElTotalEs(count);
    }

    [Then("recibo una lista vacía de proveedores de seguros con total {int}")]
    public void ReciboUnaListaVacia(int total)
    {
        _body.GetProperty("items").EnumerateArray().ShouldBeEmpty();
        ElTotalEs(total);
    }

    [Then("recibo el mensaje de proveedores de seguros {string}")]
    public void ReciboElMensaje(string message) => _body.GetProperty("message").GetString().ShouldBe(message);

    private InsuranceProvider FindCreated(string name) => _created.Single(value => value.Name == name);

    private async Task GetAsync(Dictionary<string, string?> query)
    {
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.GetAsync(QueryHelpers.AddQueryString(Url, query));
        _body = await scenario.Response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private void AssertItems(IReadOnlyList<InsuranceProvider> expected, bool includeStatus)
    {
        var items = _body.GetProperty("items").EnumerateArray().ToArray();
        items.Select(item => item.GetProperty("id").GetInt32()).ShouldBe(expected.Select(value => value.Id));
        for (var index = 0; index < items.Length; index++)
        {
            var provider = expected[index];
            items[index].GetProperty("name").GetString().ShouldBe(provider.Name);
            items[index].GetProperty("contactPhone").GetString().ShouldBe(provider.ContactPhone);
            items[index].GetProperty("contactEmail").GetString().ShouldBe(provider.ContactEmail.Value);
            items[index].GetProperty("address").GetString().ShouldBe(provider.Address);
            items[index].GetProperty("website").GetString().ShouldBe(provider.Website);
            items[index].TryGetProperty("isActive", out var active).ShouldBe(includeStatus);
            if (includeStatus) active.GetBoolean().ShouldBe(provider.IsActive);
        }
        if (_body.GetProperty("totalCount").GetInt32() > 0)
        {
            _body.TryGetProperty("message", out _).ShouldBeFalse();
        }
    }
}
