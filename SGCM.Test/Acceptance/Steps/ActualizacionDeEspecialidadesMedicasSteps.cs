using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class ActualizacionDeEspecialidadesMedicasSteps(ApiScenario scenario)
{
    private const string Description = "Atención neurológica.";
    private readonly Dictionary<int, Specialty> _originals = [];
    private int _targetId;

    [Given("que preparo la especialidad {string} {string} para actualizarla")]
    public async Task PrepararEspecialidad(string name, string state) =>
        _targetId = await SeedAsync(name, state);

    [Given("que otra especialidad con nombre {string} está {string}")]
    public async Task PrepararOtraEspecialidad(string name, string state) => await SeedAsync(name, state);

    [When("actualizo únicamente el nombre de la especialidad a {string}")]
    public Task ActualizarNombre(string name) => PatchAsync(new { name });

    [When("actualizo los campos {string} de la especialidad")]
    public Task ActualizarCampos(string fields)
    {
        var body = new Dictionary<string, string>();
        switch (fields)
        {
            case "nombre":
                body["name"] = "  PEDIATRÍA  ";
                break;
            case "descripción":
                body["description"] = "  Atención  MÉDICA.  ";
                break;
            case "ambos":
                body["name"] = "  PEDIATRÍA  ";
                body["description"] = "  Atención  MÉDICA.  ";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fields));
        }
        return PatchAsync(body);
    }

    [When("actualizo la especialidad con el campo {string} en estado {string}")]
    public Task ActualizarDatoInvalido(string field, string state)
    {
        if (field == "ninguno") return PatchAsync(new Dictionary<string, string>());
        var body = new Dictionary<string, string?>
        {
            ["name"] = "Pediatría", ["description"] = "Descripción nueva."
        };
        body[field] = state switch
        {
            "nulo" => null,
            "vacío" => string.Empty,
            "espacios" => "   ",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        return PatchAsync(body);
    }

    [When("actualizo la especialidad con nombre de {int} y descripción de {int} caracteres")]
    public Task ActualizarLongitudes(int nameLength, int descriptionLength) =>
        PatchAsync(new { name = new string('a', nameLength), description = new string('b', descriptionLength) });

    [When("actualizo una especialidad inexistente")]
    public Task ActualizarInexistente() => PatchAsync(new { name = "Pediatría" }, int.MaxValue);

    [Then("la actualización persiste nombre {string} y descripción {string} conservando ID y estado")]
    public async Task VerificarActualizacion(string name, string description)
    {
        (await scenario.Response!.Content.ReadAsStringAsync()).ShouldBeEmpty();
        using var scope = scenario.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await context.Specialties.AsNoTracking().SingleAsync(value => value.Id == _targetId);
        AssertValues(saved, _targetId, name, description, _originals[_targetId].IsActive);
        await AssertIdsAsync(context);
    }

    [Then("la actualización conserva las longitudes de {int} y {int} caracteres y el estado original")]
    public Task VerificarLongitudes(int nameLength, int descriptionLength) =>
        VerificarActualizacion("A" + new string('a', nameLength - 1), new string('b', descriptionLength));

    [Then("el error de actualización tiene código {string}")]
    public async Task VerificarError(string code) =>
        (await scenario.Response!.ReadErrorCodeAsync()).ShouldBe(code);

    [Then("ninguna especialidad cambió después de intentar actualizar")]
    public async Task VerificarSinCambios()
    {
        using var scope = scenario.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var original in _originals.Values)
        {
            var saved = await context.Specialties.AsNoTracking().SingleAsync(value => value.Id == original.Id);
            AssertValues(saved, original.Id, original.Name, original.Description, original.IsActive);
        }
        await AssertIdsAsync(context);
    }

    [AfterScenario("SpecialtyUpdating")]
    public async Task LimpiarDatosAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = _originals.Keys.ToArray();
        await context.Specialties.Where(value => ids.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
        (await context.Specialties.AnyAsync(value => ids.Contains(value.Id), timeout.Token)).ShouldBeFalse();
    }

    private async Task<int> SeedAsync(string name, string state)
    {
        var specialty = Specialty.Create(name, Description).Value;
        switch (state)
        {
            case "activa": break;
            case "inactiva": specialty.Deactivate().IsSuccess.ShouldBeTrue(); break;
            default: throw new ArgumentOutOfRangeException(nameof(state));
        }
        using var scope = scenario.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Specialties.Add(specialty);
        await context.SaveChangesAsync();
        _originals.Add(specialty.Id, specialty);
        return specialty.Id;
    }

    private async Task PatchAsync(object body, int? id = null)
    {
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.PatchAsJsonAsync($"/api/specialties/{id ?? _targetId}", body);
    }

    private async Task AssertIdsAsync(AppDbContext context) =>
        (await context.Specialties.OrderBy(value => value.Id).Select(value => value.Id).ToArrayAsync())
            .ShouldBe(_originals.Keys.OrderBy(value => value).ToArray());

    private static void AssertValues(Specialty saved, int id, string name, string description, bool isActive)
    {
        saved.Id.ShouldBe(id);
        saved.Name.ShouldBe(name);
        saved.Description.ShouldBe(description);
        saved.IsActive.ShouldBe(isActive);
    }
}
