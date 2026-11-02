using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.People;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public class UpdateSpecialtyTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string OriginalName = "Neurología";
    private const string OriginalDescription = "Atención neurológica.";
    private readonly HashSet<int> _ids = [];

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public async Task Administrator_actualiza_campos_presentes_y_conserva_omitidos_id_y_estado(
        bool nameProvided, bool descriptionProvided, bool isActive)
    {
        var original = await SeedAsync(isActive: isActive);
        Authorize(UserRole.Administrator);
        var body = new Dictionary<string, string>();
        if (nameProvided) body["name"] = "  ATENCIÓN A LA NIÑEZ  ";
        if (descriptionProvided) body["description"] = "  Atención  MÉDICA.  ";

        using var response = await Client.PatchAsJsonAsync(Url(original.Id), body);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        await AssertSavedAsync(original.Id, nameProvided ? "Atención A La Niñez" : OriginalName,
            descriptionProvided ? "Atención  MÉDICA." : OriginalDescription, isActive);
    }

    [Fact]
    public async Task Acepta_longitudes_maximas_despues_de_recortar_espacios()
    {
        var original = await SeedAsync();
        Authorize(UserRole.Administrator);

        using var response = await Client.PatchAsJsonAsync(Url(original.Id), new
        {
            name = "  " + new string('a', 100) + "  ",
            description = "  " + new string('b', 500) + "  "
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await AssertSavedAsync(original.Id, "A" + new string('a', 99), new string('b', 500), true);
    }

    [Theory]
    [InlineData("{}", "Specialty.NoFieldsProvided")]
    [InlineData("{\"name\":null}", "Specialty.NameRequired")]
    [InlineData("{\"name\":\"\"}", "Specialty.NameRequired")]
    [InlineData("{\"name\":\"   \"}", "Specialty.NameRequired")]
    [InlineData("{\"description\":null}", "Specialty.DescriptionRequired")]
    [InlineData("{\"description\":\"\"}", "Specialty.DescriptionRequired")]
    [InlineData("{\"description\":\"   \"}", "Specialty.DescriptionRequired")]
    [InlineData("{\"name\":\"Pediatría\",\"description\":null}", "Specialty.DescriptionRequired")]
    [InlineData("{\"name\":null,\"description\":\"Descripción nueva.\"}", "Specialty.NameRequired")]
    public async Task Datos_invalidos_responden_400_sin_modificar_ningun_campo(string json, string code)
    {
        var original = await SeedAsync(isActive: false);
        Authorize(UserRole.Administrator);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Client.PatchAsync(Url(original.Id), content);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(code);
        await AssertOriginalAsync(original);
    }

    [Theory]
    [InlineData(101, 50, "Specialty.NameTooLong")]
    [InlineData(50, 501, "Specialty.DescriptionTooLong")]
    public async Task Longitudes_excedidas_responden_400_y_revierten_ambos_campos(
        int nameLength, int descriptionLength, string code)
    {
        var original = await SeedAsync();
        Authorize(UserRole.Administrator);

        using var response = await Client.PatchAsJsonAsync(Url(original.Id), new
        {
            name = new string('a', nameLength), description = new string('b', descriptionLength)
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(code);
        await AssertOriginalAsync(original);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Id_invalido_responde_400_sin_modificar_datos(int id)
    {
        var original = await SeedAsync();
        Authorize(UserRole.Administrator);

        using var response = await Client.PatchAsJsonAsync(Url(id), new { name = "Pediatría" });

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(SpecialtyErrors.InvalidId.Code);
        await AssertOriginalAsync(original);
    }

    [Fact]
    public async Task Especialidad_inexistente_responde_404_sin_crear_ni_modificar_datos()
    {
        var original = await SeedAsync();
        Authorize(UserRole.Administrator);

        using var response = await Client.PatchAsJsonAsync(Url(int.MaxValue), new { name = "Pediatría" });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NotFound);
        (await response.ReadErrorCodeAsync()).ShouldBe(SpecialtyErrors.NotFound.Code);
        await AssertOriginalAsync(original);
    }

    public static IEnumerable<object[]> DuplicateNames()
    {
        string[] names = ["Cardiología", "cardiologia", "CARDIOLOGÍA", "CARDIOLOGIA"];
        foreach (var existing in names)
        foreach (var requested in names)
            yield return [existing, requested];
    }

    [Theory]
    [MemberData(nameof(DuplicateNames))]
    public async Task Nombres_equivalentes_de_otra_especialidad_responden_409_sin_modificar_ambas(
        string existingName, string requestedName)
    {
        var original = await SeedAsync();
        var existing = await SeedAsync(existingName);
        Authorize(UserRole.Administrator);

        using var response = await Client.PatchAsJsonAsync(Url(original.Id), new
        {
            name = requestedName, description = "Esta descripción tampoco debe guardarse."
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(SpecialtyErrors.NameAlreadyInUse.Code);
        await AssertOriginalAsync(original);
        await AssertOriginalAsync(existing);
    }

    [Fact]
    public async Task Nombre_de_otra_especialidad_inactiva_tambien_responde_409()
    {
        var original = await SeedAsync();
        var existing = await SeedAsync("Cardiología", false);
        Authorize(UserRole.Administrator);

        using var response = await Client.PatchAsJsonAsync(Url(original.Id), new { name = "  CARDIOLOGIA  " });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(SpecialtyErrors.NameAlreadyInUse.Code);
        await AssertOriginalAsync(original);
        await AssertOriginalAsync(existing);
    }

    [Theory]
    [InlineData("Cardiología", "Cardiología")]
    [InlineData("cardiologia", "Cardiologia")]
    [InlineData("CARDIOLOGÍA", "Cardiología")]
    [InlineData("CARDIOLOGIA", "Cardiologia")]
    public async Task Excluye_el_propio_id_al_actualizar_con_nombre_equivalente(string name, string expected)
    {
        var original = await SeedAsync("Cardiología", false);
        Authorize(UserRole.Administrator);

        using var response = await Client.PatchAsJsonAsync(Url(original.Id), new
        {
            name, description = "Descripción actualizada."
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await AssertSavedAsync(original.Id, expected, "Descripción actualizada.", false);
    }

    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Doctor)]
    public async Task JWT_real_de_otro_rol_responde_403_sin_modificar_datos(UserRole role)
    {
        var original = await SeedAsync();
        Authorize(role);

        using var response = await Client.PatchAsJsonAsync(Url(original.Id), new { name = "Pediatría" });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await AssertOriginalAsync(original);
    }

    [Theory]
    [InlineData("anonimo")]
    [InlineData("invalido")]
    [InlineData("vencido")]
    public async Task Sin_JWT_valido_responde_401_sin_modificar_datos(string authentication)
    {
        var original = await SeedAsync();
        if (authentication == "invalido")
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");
        if (authentication == "vencido")
        {
            Clock.UtcNow = DateTimeOffset.UtcNow.AddHours(-1);
            SetToken(UserRole.Administrator, Client);
        }

        using var response = await Client.PatchAsJsonAsync(Url(original.Id), new { name = "Pediatría" });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        await AssertOriginalAsync(original);
    }

    [Fact]
    public async Task Conflicto_del_indice_PostgreSQL_durante_UPDATE_responde_409_y_revierte_los_datos()
    {
        var original = await SeedAsync(isActive: false);
        var existing = await SeedAsync("Cardiología");
        var checks = 0;
        var failedUpdate = new FailedUpdateObserver();
        var transactions = new TransactionObserver();
        await using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.AddScoped(provider => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(
                provider.GetRequiredService<DbContextOptions<AppDbContext>>())
                .AddInterceptors(failedUpdate, transactions).Options));
            services.RemoveAll<ISpecialtyRepository>();
            services.AddScoped<ISpecialtyRepository>(provider => new SkipDuplicateCheckRepository(
                new SpecialtyRepository(provider.GetRequiredService<AppDbContext>()),
                () => Interlocked.Increment(ref checks)));
        }));
        using var client = app.CreateClient();
        Authorize(UserRole.Administrator, client);

        using var response = await client.PatchAsJsonAsync(Url(original.Id), new
        {
            name = "CARDIOLOGIA", description = "No debe persistirse."
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(SpecialtyErrors.NameAlreadyInUse.Code);
        checks.ShouldBe(1);
        failedUpdate.CommandText.ShouldStartWith("UPDATE people.specialty");
        var postgres = failedUpdate.Exception.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.SchemaName.ShouldBe("people");
        postgres.TableName.ShouldBe("specialty");
        postgres.ConstraintName.ShouldBe("uq_specialty_name");
        transactions.Rollbacks.ShouldBe(1);
        transactions.Commits.ShouldBe(0);
        await AssertOriginalAsync(original);
        await AssertOriginalAsync(existing);
    }

    public override async Task DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var scope = Factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Specialties.Where(value => _ids.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
            (await context.Specialties.AnyAsync(value => _ids.Contains(value.Id), timeout.Token)).ShouldBeFalse();
        }
        finally
        {
            await base.DisposeAsync();
        }
    }

    private static string Url(int id) => $"/api/specialties/{id}";

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

    private async Task<Specialty> SeedAsync(string name = OriginalName, bool isActive = true)
    {
        var specialty = Specialty.Create(name, OriginalDescription).Value;
        if (!isActive) specialty.Deactivate().IsSuccess.ShouldBeTrue();
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>().Add(specialty);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        _ids.Add(specialty.Id);
        return specialty;
    }

    private Task AssertOriginalAsync(Specialty original) =>
        AssertSavedAsync(original.Id, original.Name, original.Description, original.IsActive);

    private async Task AssertSavedAsync(int id, string name, string description, bool isActive)
    {
        // Un scope nuevo evita que la caché del contexto oculte fallos de persistencia.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await context.Specialties.AsNoTracking().SingleAsync(value => value.Id == id);
        saved.Id.ShouldBe(id);
        saved.Name.ShouldBe(name);
        saved.Description.ShouldBe(description);
        saved.IsActive.ShouldBe(isActive);
        (await context.Specialties.OrderBy(value => value.Id).Select(value => value.Id).ToArrayAsync())
            .ShouldBe(_ids.OrderBy(value => value).ToArray());
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
