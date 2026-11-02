using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class VisualizacionDeEspecialidadesDisponiblesSteps(ApiScenario scenario)
{
    private readonly List<Specialty> _created = [];
    private JsonElement _body;

    [Given("que el catálogo de especialidades contiene:")]
    public async Task DadoElCatalogo(Table table)
    {
        using var scope = scenario.Factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>();
        foreach (var row in table.Rows)
        {
            var specialty = Specialty.Create(row["Nombre"], row["Descripción"]).Value;
            switch (row["Estado"])
            {
                case "Activa": break;
                case "Inactiva": specialty.Deactivate().IsSuccess.ShouldBeTrue(); break;
                default: throw new ArgumentOutOfRangeException(nameof(table), row["Estado"], "Estado de prueba desconocido.");
            }
            _created.Add(specialty);
            repository.Add(specialty);
        }
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        _created.ShouldAllBe(specialty => specialty.Id > 0);
    }

    [When("consulto la página {int} de tamaño {int} con estado {string}")]
    public Task ConsultoLaPagina(int page, int size, string status) => GetAsync(new Dictionary<string, string?>
    {
        ["pageNumber"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["pageSize"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["status"] = status
    });

    [When("busco especialidades por {string}")]
    public Task BuscoEspecialidades(string search) => GetAsync(new Dictionary<string, string?> { ["search"] = search });

    [When("consulto el catálogo con estado {string}")]
    public Task ConsultoElEstado(string status) => GetAsync(new Dictionary<string, string?> { ["status"] = status });

    [Then("recibo {string} con descripción {string}")]
    public void ReciboLaEspecialidad(string name, string description)
    {
        var expected = _created.Single(value => value.Name == name);
        expected.Description.ShouldBe(description);
        AssertItems([expected], false);
    }

    [Then("la página recibida es {int} y su tamaño es {int}")]
    public void LaPaginaEs(int page, int size)
    {
        _body.GetProperty("pageNumber").GetInt32().ShouldBe(page);
        _body.GetProperty("pageSize").GetInt32().ShouldBe(size);
    }

    [Then("el total de especialidades disponibles es {int}")]
    public void ElTotalEs(int total) => _body.GetProperty("totalCount").GetInt32().ShouldBe(total);

    [Then("no se expone el estado de las especialidades")]
    public void NoSeExponeElEstado()
    {
        var items = _body.GetProperty("items").EnumerateArray().ToArray();
        items.ShouldNotBeEmpty();
        foreach (var item in items) item.TryGetProperty("isActive", out _).ShouldBeFalse();
    }

    [Then("recibo únicamente la especialidad {string}")]
    public void ReciboSoloLaEspecialidad(string name)
    {
        AssertItems([_created.Single(value => value.Name == name)], false);
        ElTotalEs(1);
    }

    [Then("recibo {int} especialidades correspondientes al filtro {string}")]
    public void ReciboElFiltro(int count, string status)
    {
        bool? active = status switch
        {
            "Active" => true,
            "Inactive" => false,
            "All" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
        var expected = _created.Where(value => !active.HasValue || value.IsActive == active.Value)
            .OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
        expected.Length.ShouldBe(count);
        AssertItems(expected, true);
        ElTotalEs(count);
    }

    [Then("recibo únicamente las {int} especialidades activas")]
    public void ReciboSoloActivas(int count)
    {
        var expected = _created.Where(value => value.IsActive).OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
        expected.Length.ShouldBe(count);
        AssertItems(expected, false);
        ElTotalEs(count);
    }

    [Then("recibo una lista vacía con total {int}")]
    public void ReciboUnaListaVacia(int total)
    {
        _body.GetProperty("items").EnumerateArray().ShouldBeEmpty();
        ElTotalEs(total);
    }

    [Then("recibo el mensaje {string}")]
    public void ReciboElMensaje(string message) => _body.GetProperty("message").GetString().ShouldBe(message);

    [AfterScenario("SpecialtyListing")]
    public async Task LimpiarCatalogoAsync()
    {
        var ids = _created.Where(value => value.Id > 0).Select(value => value.Id).ToArray();
        if (ids.Length == 0) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Specialties
            .Where(value => ids.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
        (await context.Specialties.AnyAsync(value => ids.Contains(value.Id), timeout.Token)).ShouldBeFalse();
    }

    private async Task GetAsync(Dictionary<string, string?> query)
    {
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.GetAsync(QueryHelpers.AddQueryString("/api/specialties", query));
        _body = await scenario.Response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private void AssertItems(IReadOnlyList<Specialty> expected, bool includeStatus)
    {
        var items = _body.GetProperty("items").EnumerateArray().ToArray();
        items.Select(item => item.GetProperty("id").GetInt32()).ShouldBe(expected.Select(value => value.Id));
        for (var index = 0; index < items.Length; index++)
        {
            items[index].GetProperty("name").GetString().ShouldBe(expected[index].Name);
            items[index].GetProperty("description").GetString().ShouldBe(expected[index].Description);
            items[index].TryGetProperty("isActive", out var active).ShouldBe(includeStatus);
            if (includeStatus) active.GetBoolean().ShouldBe(expected[index].IsActive);
        }
        if (_body.GetProperty("totalCount").GetInt32() > 0)
        {
            _body.TryGetProperty("message", out _).ShouldBeFalse();
        }
    }
}
