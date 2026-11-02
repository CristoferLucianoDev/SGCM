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
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.People;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public class CreateSpecialtyTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Url = "/api/specialties";
    private const string Description = "Atención médica.";
    private readonly HashSet<int> _createdIds = [];

    [Fact]
    public async Task Administrator_crea_una_especialidad_activa_y_recibe_el_id_real()
    {
        Authorize(UserRole.Administrator);

        using var response = await PostAsync("  ATENCIÓN A LA NIÑEZ  ", "  Atención  MÉDICA.  ");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var id = _createdIds.ShouldHaveSingleItem();
        id.ShouldBeGreaterThan(0);
        using var scope = Factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Specialties
            .AsNoTracking().SingleAsync(value => value.Id == id);
        saved.Name.ShouldBe("Atención A La Niñez");
        saved.Description.ShouldBe("Atención  MÉDICA.");
        saved.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Administrator_puede_crear_con_las_longitudes_maximas()
    {
        Authorize(UserRole.Administrator);

        using var response = await PostAsync("  " + new string('a', 100) + "  ", "  " + new string('b', 500) + "  ");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        using var scope = Factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Specialties
            .AsNoTracking().SingleAsync(value => _createdIds.Contains(value.Id));
        saved.Name.ShouldBe("A" + new string('a', 99));
        saved.Description.ShouldBe(new string('b', 500));
    }

    [Theory]
    [InlineData(null, Description, "Specialty.NameRequired", "El nombre de la especialidad es obligatorio.")]
    [InlineData("", Description, "Specialty.NameRequired", "El nombre de la especialidad es obligatorio.")]
    [InlineData("   ", Description, "Specialty.NameRequired", "El nombre de la especialidad es obligatorio.")]
    [InlineData("Cardiología", null, "Specialty.DescriptionRequired", "La descripción de la especialidad es obligatoria.")]
    [InlineData("Cardiología", "", "Specialty.DescriptionRequired", "La descripción de la especialidad es obligatoria.")]
    [InlineData("Cardiología", "   ", "Specialty.DescriptionRequired", "La descripción de la especialidad es obligatoria.")]
    public async Task Datos_obligatorios_invalidos_responden_400_con_codigo_de_dominio(
        string? name, string? description, string errorCode, string title)
    {
        Authorize(UserRole.Administrator);
        var idsBeforeRequest = await ReadIdsAsync();

        using var response = await PostAsync(name, description);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        await AssertValidationErrorAsync(response, errorCode, title, idsBeforeRequest);
        _createdIds.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, "Specialty.NameTooLong", "El nombre de la especialidad no puede superar 100 caracteres.")]
    [InlineData(false, "Specialty.DescriptionTooLong", "La descripción de la especialidad no puede superar 500 caracteres.")]
    public async Task Longitudes_excedidas_responden_400(bool longName, string errorCode, string title)
    {
        Authorize(UserRole.Administrator);
        var idsBeforeRequest = await ReadIdsAsync();

        using var response = await PostAsync(longName ? new string('a', 101) : "Cardiología",
            longName ? Description : new string('b', 501));

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        await AssertValidationErrorAsync(response, errorCode, title, idsBeforeRequest);
        _createdIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sin_token_responde_401_sin_crear_nada()
    {
        using var response = await PostAsync("Cardiología", Description);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await CountByNameAsync("Cardiología")).ShouldBe(0);
    }

    [Fact]
    public async Task Token_invalido_responde_401_sin_crear_nada()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");

        using var response = await PostAsync("Cardiología", Description);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await CountByNameAsync("Cardiología")).ShouldBe(0);
    }

    [Fact]
    public async Task Token_Administrator_vencido_responde_401()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow.AddHours(-1);
        var token = Factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(1, UserRole.Administrator, 1, null);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        using var response = await PostAsync("Cardiología", Description);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await CountByNameAsync("Cardiología")).ShouldBe(0);
    }

    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Doctor)]
    public async Task Otros_roles_responden_403_sin_crear_nada(UserRole role)
    {
        Authorize(role);

        using var response = await PostAsync("Cardiología", Description);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await CountByNameAsync("Cardiología")).ShouldBe(0);
    }

    [Theory]
    [InlineData("cardiologia", true)]
    [InlineData("CARDIOLOGÍA", true)]
    [InlineData("  CARDIOLOGIA  ", true)]
    [InlineData("cardiologia", false)]
    public async Task Nombre_existente_responde_409_incluso_si_esta_inactivo(string name, bool isActive)
    {
        Authorize(UserRole.Administrator);
        var specialty = Specialty.Create("Cardiología", Description).Value;
        if (!isActive)
        {
            specialty.Deactivate().IsSuccess.ShouldBeTrue();
        }

        await SeedAsync(specialty);

        using var response = await PostAsync(name, "Otra descripción.");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(SpecialtyErrors.NameAlreadyInUse.Code);
        (await CountByNameAsync(name)).ShouldBe(1);
        using var scope = Factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Specialties
            .AsNoTracking().SingleAsync(value => value.Id == specialty.Id);
        saved.Description.ShouldBe(Description);
        saved.IsActive.ShouldBe(isActive);
    }

    [Fact]
    public async Task Conflicto_en_PostgreSQL_durante_el_guardado_llega_como_409()
    {
        await SeedAsync(Specialty.Create("Cardiología", Description).Value);
        var checks = 0;
        await using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISpecialtyRepository>();
            services.AddScoped<ISpecialtyRepository>(provider => new SkipDuplicateCheckRepository(
                new SpecialtyRepository(provider.GetRequiredService<AppDbContext>()), () => Interlocked.Increment(ref checks)));
        }));
        using var client = app.CreateClient();
        Authorize(UserRole.Administrator, client);

        using var response = await PostAsync("CARDIOLOGIA", "Duplicada.", client);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(SpecialtyErrors.NameAlreadyInUse.Code);
        checks.ShouldBe(1);
        (await CountByNameAsync("cardiologia")).ShouldBe(1);
    }

    public override async Task DisposeAsync()
    {
        try
        {
            if (_createdIds.Count > 0)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await using var scope = Factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Specialties
                    .Where(value => _createdIds.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
            }
        }
        finally
        {
            await base.DisposeAsync();
        }
    }

    private async Task AssertValidationErrorAsync(HttpResponseMessage response, string code, string title, int[] idsBeforeRequest)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe(400);
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("title").GetString().ShouldBe(title);
        (await ReadIdsAsync()).ShouldBe(idsBeforeRequest);
    }

    private async Task<int[]> ReadIdsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Specialties
            .OrderBy(value => value.Id).Select(value => value.Id).ToArrayAsync();
    }

    private void Authorize(UserRole role, HttpClient? client = null)
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var token = Factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(1, role, 1, null);
        (client ?? Client).DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }

    private async Task<HttpResponseMessage> PostAsync(string? name, string? description, HttpClient? client = null)
    {
        var response = await (client ?? Client).PostAsJsonAsync(Url, new CreateSpecialtyRequest(name!, description!));
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var created = (await response.Content.ReadFromJsonAsync<CreateSpecialtyResponse>())!;
            _createdIds.Add(created.Id);
        }

        return response;
    }

    private async Task SeedAsync(Specialty specialty)
    {
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>().Add(specialty);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        _createdIds.Add(specialty.Id);
    }

    private async Task<int> CountByNameAsync(string name)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Specialties.CountAsync(value =>
            PostgresFunctions.NormalizeText(value.Name) == PostgresFunctions.NormalizeText(name));
    }

    // Simula la ventana de concurrencia sin reemplazar el INSERT ni el índice PostgreSQL.
    private sealed class SkipDuplicateCheckRepository(ISpecialtyRepository inner, Action onCheck) : ISpecialtyRepository
    {
        public Task<Specialty?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            inner.GetByIdAsync(id, cancellationToken);

        public Task<bool> ExistsByNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onCheck();
            return Task.FromResult(false);
        }

        public void Add(Specialty specialty) => inner.Add(specialty);
    }
}
