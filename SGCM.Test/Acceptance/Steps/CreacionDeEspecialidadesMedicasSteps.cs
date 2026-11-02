using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class CreacionDeEspecialidadesMedicasSteps(ApiScenario scenario)
{
    private const string Url = "/api/specialties";
    private const string ExistingDescription = "Atención del corazón.";
    private readonly HashSet<int> _createdSpecialtyIds = [];
    private readonly HashSet<int> _createdUserIds = [];
    private int[] _idsBeforeRequest = [];
    private int _createdId;
    private int _originalId;

    [Given("que estoy autenticado para crear especialidades como {string}")]
    [Given("que estoy autenticado para consultar especialidades como {string}")]
    [Given("que estoy autenticado para actualizar especialidades como {string}")]
    public async Task DadoUnUsuarioAutenticado(string role)
    {
        const string password = "Secreta123";
        var email = $"especialidades-{Guid.NewGuid():N}@correo.com";
        scenario.Factory.Clock.UtcNow = DateTimeOffset.UtcNow;
        var user = UserMother.WithRole(Enum.Parse<UserRole>(role), email: Email.Create(email).Value,
            passwordHash: scenario.Factory.HashPassword(password));
        await scenario.Factory.SaveAsync(user);
        _createdUserIds.Add(user.Id);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        request.Headers.UserAgent.ParseAdd("PruebasSGCM/1.0");
        using var response = await scenario.Client.SendAsync(request);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var login = await response.ReadLoginAsync();
        login.RequiresTwoFactor.ShouldBeFalse();
        login.AccessToken.ShouldNotBeNullOrEmpty();
        scenario.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    [Given("que no estoy autenticado para crear especialidades")]
    [Given("que no estoy autenticado para consultar especialidades")]
    [Given("que no estoy autenticado para actualizar especialidades")]
    public void DadoUnUsuarioAnonimo() => scenario.Client.DefaultRequestHeaders.Authorization = null;

    [Given("que existe la especialidad {string}")]
    public async Task DadoUnaEspecialidadExistente(string name)
    {
        var specialty = Specialty.Create(name, ExistingDescription).Value;
        using var scope = scenario.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>().Add(specialty);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        _originalId = specialty.Id;
        _createdSpecialtyIds.Add(specialty.Id);
    }

    [When("solicito crear la especialidad {string} con descripción {string}")]
    public Task SolicitoCrear(string name, string description) =>
        PostAsync(new CreateSpecialtyRequest(name, description));

    [When("solicito crear una especialidad con el campo {string} {string}")]
    public Task SolicitoCrearConCampoInvalido(string field, string state)
    {
        var body = new Dictionary<string, string>
        {
            ["name"] = "Cardiología",
            ["description"] = ExistingDescription
        };
        var key = field switch
        {
            "Name" => "name",
            "Description" => "description",
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        switch (state)
        {
            case "vacío":
                body[key] = string.Empty;
                break;
            case "ausente":
                body.Remove(key);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        return PostAsync(body);
    }

    [When("solicito crear una especialidad con nombre de {int} y descripción de {int} caracteres")]
    public Task SolicitoCrearConLongitudes(int nameLength, int descriptionLength) =>
        SolicitoCrear(new string('a', nameLength), new string('b', descriptionLength));

    [Then("la API de especialidades responde HTTP {int}")]
    public Task LaApiResponde(int status) => scenario.Response!.ShouldHaveStatusAsync((HttpStatusCode)status);

    [Then("recibo el ID real de la especialidad creada")]
    public async Task ReciboElIdReal()
    {
        var created = await scenario.Response!.Content.ReadFromJsonAsync<CreateSpecialtyResponse>();
        created.ShouldNotBeNull();
        _createdId = created.Id;
        _createdId.ShouldBeGreaterThan(0);
        _createdSpecialtyIds.ShouldContain(_createdId);
        (await FindCreatedAsync()).Id.ShouldBe(_createdId);
    }

    [Then("la especialidad creada está activa con nombre {string} y descripción {string}")]
    public async Task LaEspecialidadEstaActiva(string name, string description)
    {
        var specialty = await FindCreatedAsync();
        specialty.Name.ShouldBe(name);
        specialty.Description.ShouldBe(description);
        specialty.IsActive.ShouldBeTrue();
    }

    [Then("la especialidad creada está activa y tiene nombre de {int} y descripción de {int} caracteres")]
    public Task LaEspecialidadConservaLasLongitudes(int nameLength, int descriptionLength) =>
        LaEspecialidadEstaActiva("A" + new string('a', nameLength - 1), new string('b', descriptionLength));

    [Then("el error de especialidad tiene el código {string} y el mensaje {string}")]
    public async Task ElErrorIdentificaElDato(string code, string title)
    {
        var body = await scenario.Response!.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe((int)scenario.Response.StatusCode);
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("title").GetString().ShouldBe(title);
    }

    [Then("no se agregó ninguna especialidad")]
    public async Task NoSeAgregoNingunaEspecialidad() =>
        (await ReadIdsAsync()).ShouldBe(_idsBeforeRequest);

    [Then("solo permanece la especialidad original {string}")]
    public async Task SoloPermaneceLaOriginal(string name)
    {
        using var scope = scenario.Factory.Services.CreateScope();
        var matches = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Specialties.AsNoTracking()
            .Where(value => PostgresFunctions.NormalizeText(value.Name) == PostgresFunctions.NormalizeText(name))
            .ToListAsync();
        var specialty = matches.ShouldHaveSingleItem();
        specialty.Id.ShouldBe(_originalId);
        specialty.Name.ShouldBe(name);
        specialty.Description.ShouldBe(ExistingDescription);
        specialty.IsActive.ShouldBeTrue();
    }

    [AfterScenario("Specialties")]
    public async Task LimpiarDatosAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_createdSpecialtyIds.Count > 0)
        {
            await context.Specialties.Where(value => _createdSpecialtyIds.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
        }

        if (_createdUserIds.Count > 0)
        {
            await context.Users.Where(value => _createdUserIds.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
        }
    }

    private async Task PostAsync(object body)
    {
        _idsBeforeRequest = await ReadIdsAsync();
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.PostAsJsonAsync(Url, body);
        // @Integration impide inserciones de otras pruebas entre ambas lecturas.
        _createdSpecialtyIds.UnionWith((await ReadIdsAsync()).Except(_idsBeforeRequest));
    }

    private async Task<int[]> ReadIdsAsync()
    {
        using var scope = scenario.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Specialties
            .OrderBy(value => value.Id).Select(value => value.Id).ToArrayAsync();
    }

    private async Task<Specialty> FindCreatedAsync()
    {
        using var scope = scenario.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Specialties.AsNoTracking()
            .SingleAsync(value => value.Id == _createdId);
    }
}
