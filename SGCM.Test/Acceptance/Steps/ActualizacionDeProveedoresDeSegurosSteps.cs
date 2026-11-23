using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class ActualizacionDeProveedoresDeSegurosSteps(ApiScenario scenario)
{
    private const string Url = "/api/insurance-providers";
    private const string OtherName = "Seguros Nacional";
    private const string NewPhone = "809-555-0999";
    private const string NewEmail = "nuevo@seguros.com";
    private const string NewAddress = "Calle Nueva 5, Santiago";
    private const string NewWebsite = "https://nuevo.com/inicio";

    // El relleno del sitio web va en la ruta para no depender de cómo Uri trata un host muy largo.
    private const string WebsitePrefix = "https://seguros.com/";

    private readonly Dictionary<int, Snapshot> _originals = [];
    private int _targetId;

    [Given("que preparo el proveedor de seguros {string} {string} para actualizarlo")]
    public async Task PrepararProveedor(string name, string state) => _targetId = await SeedAsync(name, state);

    [Given("que otro proveedor de seguros con nombre {string} está {string}")]
    public async Task PrepararOtroProveedor(string name, string state) => await SeedAsync(name, state);

    [When("actualizo los campos {string} del proveedor de seguros")]
    public Task ActualizarCampos(string fields) => PatchAsync(BodyFor(fields));

    [When("actualizo el proveedor de seguros con el campo {string} en estado {string}")]
    public Task ActualizarCampoEnEstado(string field, string state)
    {
        var value = state switch
        {
            "nulo" => null,
            "vacío" => string.Empty,
            "espacios" => "   ",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Estado no previsto.")
        };
        // Quitar el sitio web es una actualización válida: no lleva campo acompañante para poder verificar el resto.
        return PatchAsync(WithCompanion(field, value, includeCompanion: field != nameof(InsuranceProvider.Website)));
    }

    [When("actualizo el proveedor de seguros con el campo {string} que supera su longitud")]
    public Task ActualizarCampoDemasiadoLargo(string field) => PatchAsync(WithCompanion(field, TooLongValue(field)));

    [When("actualizo el proveedor de seguros con el campo {string} con formato inválido")]
    public Task ActualizarCampoConFormatoInvalido(string field) => PatchAsync(WithCompanion(field, field switch
    {
        "ContactEmail" => "sin-arroba",
        "Website" => "javascript:alert(1)",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "El campo no tiene un formato que validar.")
    }));

    [When("actualizo el proveedor de seguros con los campos en su longitud máxima")]
    public Task ActualizarLongitudesMaximas() => PatchAsync(new Dictionary<string, string?>
    {
        ["name"] = Padded(MaxName), ["contactPhone"] = Padded(MaxPhone),
        ["address"] = Padded(MaxAddress), ["website"] = Padded(MaxWebsite)
    });

    [When("actualizo únicamente el nombre del proveedor de seguros a {string}")]
    public Task ActualizarSoloElNombre(string name) => PatchAsync(new Dictionary<string, string?> { ["name"] = name });

    [When("actualizo el nombre del proveedor de seguros a {string} y el teléfono a {string}")]
    public Task ActualizarNombreYTelefono(string name, string phone) =>
        PatchAsync(new Dictionary<string, string?> { ["name"] = name, ["contactPhone"] = phone });

    [When("actualizo el proveedor de seguros sin enviar ningún campo")]
    public Task ActualizarSinCampos() => PatchAsync(new Dictionary<string, string?>());

    [When("actualizo un proveedor de seguros inexistente")]
    public Task ActualizarInexistente() => PatchAsync(
        new Dictionary<string, string?> { ["name"] = OtherName, ["contactPhone"] = NewPhone }, int.MaxValue);

    [Then("el proveedor de seguros persiste los campos {string} conservando ID, estado y los demás datos")]
    public async Task VerificarCamposActualizados(string fields)
    {
        var original = _originals[_targetId];
        await AssertOnlyTargetChangedAsync(ExpectedFor(original, fields));
    }

    [Then("el sitio web del proveedor de seguros fue quitado conservando los demás datos")]
    public async Task VerificarSitioWebQuitado() =>
        await AssertOnlyTargetChangedAsync(_originals[_targetId] with { Website = null });

    [Then("el proveedor de seguros persiste los campos en su longitud máxima conservando ID, estado y correo")]
    public async Task VerificarLongitudesMaximas() =>
        await AssertOnlyTargetChangedAsync(_originals[_targetId] with
        {
            Name = MaxName, ContactPhone = MaxPhone, Address = MaxAddress, Website = MaxWebsite
        });

    [Then("ningún proveedor de seguros cambió después de intentar actualizar")]
    public async Task VerificarSinCambios() => (await ReadAllAsync()).ShouldBe(_originals.Values.OrderBy(value => value.Id));

    private static string MaxName => new('a', InsuranceProvider.MaxNameLength);

    private static string MaxPhone => new('1', InsuranceProvider.MaxContactPhoneLength);

    private static string MaxAddress => new('b', InsuranceProvider.MaxAddressLength);

    private static string MaxWebsite => WebsitePrefix + new string('c', InsuranceProvider.MaxWebsiteLength - WebsitePrefix.Length);

    private static string Padded(string value) => $"  {value}  ";

    private static string TooLongValue(string field) => field switch
    {
        "Name" => new string('a', InsuranceProvider.MaxNameLength + 1),
        "ContactPhone" => new string('1', InsuranceProvider.MaxContactPhoneLength + 1),
        "Address" => new string('b', InsuranceProvider.MaxAddressLength + 1),
        "Website" => WebsitePrefix + new string('c', InsuranceProvider.MaxWebsiteLength + 1 - WebsitePrefix.Length),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "El campo no tiene una longitud máxima que probar.")
    };

    // El cuerpo lleva un segundo campo válido y distinto para demostrar que el rechazo es atómico.
    private static Dictionary<string, string?> WithCompanion(string field, string? value, bool includeCompanion = true)
    {
        var body = new Dictionary<string, string?> { [ToJsonKey(field)] = value };
        if (!includeCompanion) return body;

        if (field == nameof(InsuranceProvider.Name)) body["contactPhone"] = NewPhone;
        else body["name"] = OtherName;
        return body;
    }

    private static string ToJsonKey(string field) => char.ToLowerInvariant(field[0]) + field[1..];

    // Cada campo se envía con espacios y mayúsculas para comprobar la normalización al guardar.
    private static Dictionary<string, string?> BodyFor(string fields)
    {
        var body = new Dictionary<string, string?>();
        if (fields is "nombre" or "todos") body["name"] = "  Seguros Nacional  ";
        if (fields is "teléfono" or "todos") body["contactPhone"] = "  809-555-0999  ";
        if (fields is "correo" or "todos") body["contactEmail"] = "  NUEVO@Seguros.COM  ";
        if (fields is "dirección" or "todos") body["address"] = "  Calle Nueva 5, Santiago  ";
        if (fields is "sitio web" or "todos") body["website"] = "  https://nuevo.com/inicio  ";
        if (body.Count == 0) throw new ArgumentOutOfRangeException(nameof(fields), fields, "Campos no previstos.");
        return body;
    }

    private static Snapshot ExpectedFor(Snapshot original, string fields) => original with
    {
        Name = fields is "nombre" or "todos" ? OtherName : original.Name,
        ContactPhone = fields is "teléfono" or "todos" ? NewPhone : original.ContactPhone,
        ContactEmail = fields is "correo" or "todos" ? NewEmail : original.ContactEmail,
        Address = fields is "dirección" or "todos" ? NewAddress : original.Address,
        Website = fields is "sitio web" or "todos" ? NewWebsite : original.Website
    };

    private async Task<int> SeedAsync(string name, string state)
    {
        var provider = InsuranceProviderMother.Valid(name);
        switch (state)
        {
            case "activo": break;
            case "inactivo": provider.Deactivate().IsSuccess.ShouldBeTrue(); break;
            default: throw new ArgumentOutOfRangeException(nameof(state), state, "Estado no previsto.");
        }

        using var scope = scenario.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.InsuranceProviders.Add(provider);
        await context.SaveChangesAsync();
        _originals.Add(provider.Id, ToSnapshot(provider));
        return provider.Id;
    }

    private async Task PatchAsync(Dictionary<string, string?> body, int? id = null)
    {
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.PatchAsJsonAsync($"{Url}/{id ?? _targetId}", body);
    }

    // Se compara también con los proveedores no objetivo, para detectar cambios en cualquier otra fila.
    private async Task AssertOnlyTargetChangedAsync(Snapshot expected)
    {
        (await scenario.Response!.Content.ReadAsStringAsync()).ShouldBeEmpty();
        var expectedRows = _originals.Values.Select(value => value.Id == _targetId ? expected : value).OrderBy(value => value.Id);
        (await ReadAllAsync()).ShouldBe(expectedRows);
    }

    private async Task<Snapshot[]> ReadAllAsync()
    {
        using var scope = scenario.Factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().InsuranceProviders
            .AsNoTracking().OrderBy(value => value.Id).ToListAsync();
        return rows.Select(ToSnapshot).ToArray();
    }

    private static Snapshot ToSnapshot(InsuranceProvider provider) => new(
        provider.Id, provider.Name, provider.ContactPhone, provider.ContactEmail.Value, provider.Address,
        provider.Website, provider.IsActive);

    private sealed record Snapshot(
        int Id, string Name, string ContactPhone, string ContactEmail, string Address, string? Website, bool IsActive);
}
