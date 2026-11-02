using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class SpecialtyActivationTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private readonly List<int> _specialtyIds = [];
    private readonly List<int> _userIds = [];
    private readonly List<int> _doctorIds = [];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Administrator_cambia_el_estado_y_repetir_devuelve_conflicto_sin_alterar_datos_ni_relaciones(bool activate)
    {
        var original = await SeedAsync(!activate);
        await AddDoctorsAsync(original.Id);
        await AuthorizeAsync(UserRole.Administrator, Client);

        using var success = await Client.PatchAsync(Url(original.Id, activate), null);

        await success.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await success.Content.ReadAsStringAsync()).ShouldBeEmpty();
        await AssertSavedAsync(original, activate);
        var before = await SnapshotAsync();
        var relations = await ReadRelationsAsync();
        relations.Length.ShouldBe(2);
        var doctors = await ReadDoctorsAsync();

        using var conflict = await Client.PatchAsync(Url(original.Id, activate), null);

        await conflict.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await conflict.ReadErrorCodeAsync()).ShouldBe(activate ? "Specialty.AlreadyActive" : "Specialty.AlreadyInactive");
        (await SnapshotAsync()).ShouldBe(before);
        (await ReadRelationsAsync()).ShouldBe(relations);
        (await ReadDoctorsAsync()).ShouldBe(doctors);
        await AssertSavedAsync(original, activate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inexistente_devuelve_404_sin_crear_ni_modificar_especialidades(bool activate)
    {
        await SeedAsync();
        await AuthorizeAsync(UserRole.Administrator, Client);
        var before = await SnapshotAsync();
        using (var scope = Factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Specialties.AnyAsync(value => value.Id == int.MaxValue))
                .ShouldBeFalse();

        using var response = await Client.PatchAsync(Url(int.MaxValue, activate), null);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NotFound);
        (await response.ReadErrorCodeAsync()).ShouldBe(SpecialtyErrors.NotFound.Code);
        (await SnapshotAsync()).ShouldBe(before);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    public async Task Id_no_positivo_devuelve_400_sin_modificar_datos(bool activate, int id)
    {
        await SeedAsync();
        await AuthorizeAsync(UserRole.Administrator, Client);
        var before = await SnapshotAsync();

        using var response = await Client.PatchAsync(Url(id, activate), null);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(SpecialtyErrors.InvalidId.Code);
        (await SnapshotAsync()).ShouldBe(before);
    }

    [Theory]
    [InlineData(false, UserRole.Patient)]
    [InlineData(false, UserRole.Doctor)]
    [InlineData(true, UserRole.Patient)]
    [InlineData(true, UserRole.Doctor)]
    public async Task JWT_real_sin_pasos_pendientes_de_otro_rol_devuelve_403_y_no_modifica_datos(bool activate, UserRole role)
    {
        await SeedAsync(!activate);
        await AuthorizeAsync(role, Client);
        var before = await SnapshotAsync();

        using var response = await Client.PatchAsync(Url(_specialtyIds.Single(), activate), null);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await SnapshotAsync()).ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Anonimo_devuelve_401_y_no_modifica_datos(bool activate)
    {
        var original = await SeedAsync(!activate);
        Client.DefaultRequestHeaders.Authorization.ShouldBeNull();
        var before = await SnapshotAsync();

        using var response = await Client.PatchAsync(Url(original.Id, activate), null);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await SnapshotAsync()).ShouldBe(before);
    }

    [Theory]
    [InlineData(false, 204)]
    [InlineData(true, 204)]
    [InlineData(false, 409)]
    [InlineData(true, 409)]
    [InlineData(false, 404)]
    [InlineData(true, 404)]
    [InlineData(false, 400)]
    [InlineData(true, 400)]
    public async Task Pipeline_real_guarda_y_confirma_o_revierte_segun_el_resultado(bool activate, int status)
    {
        var original = await SeedAsync(status == 409 ? activate : !activate);
        await AuthorizeAsync(UserRole.Administrator, Client);
        var before = await SnapshotAsync();
        var saves = new SaveObserver();
        var transactions = new TransactionObserver();
        await using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.AddScoped(provider => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(
                provider.GetRequiredService<DbContextOptions<AppDbContext>>())
                .AddInterceptors(saves, transactions).Options));
        }));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = Client.DefaultRequestHeaders.Authorization;
        var id = status switch { 400 => 0, 404 => int.MaxValue, _ => original.Id };

        using var response = await client.PatchAsync(Url(id, activate), null);

        await response.ShouldHaveStatusAsync((HttpStatusCode)status);
        transactions.Starts.ShouldBe(status == 400 ? 0 : 1);
        saves.Calls.ShouldBe(status == 204 ? 1 : 0);
        transactions.Commits.ShouldBe(status == 204 ? 1 : 0);
        transactions.Rollbacks.ShouldBe(status is 404 or 409 ? 1 : 0);
        if (status == 204)
        {
            await AssertSavedAsync(original, activate);
        }
        else
        {
            var code = status switch
            {
                400 => SpecialtyErrors.InvalidId.Code,
                404 => SpecialtyErrors.NotFound.Code,
                _ => activate ? "Specialty.AlreadyActive" : "Specialty.AlreadyInactive"
            };
            (await response.ReadErrorCodeAsync()).ShouldBe(code);
            (await SnapshotAsync()).ShouldBe(before);
        }
    }

    [Fact]
    public async Task PATCH_conserva_dos_medicos_y_sus_relaciones_y_el_directorio_oculta_solo_la_especialidad_inactiva()
    {
        var original = await SeedAsync();
        var control = await SeedAsync();
        await AddDoctorsAsync(original.Id);
        using (var scope = Factory.Services.CreateScope())
            (await TestDataSeeder.InsertDoctorSpecialtyAsync(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(), _doctorIds[0], control.Id)).ShouldBe(1);
        await AuthorizeAsync(UserRole.Administrator, Client);
        var doctors = await ReadDoctorsAsync();
        var relations = await ReadRelationsAsync();
        doctors.Length.ShouldBe(2);
        relations.Length.ShouldBe(3);
        await AssertDirectoryAsync(original.Id, control.Id, true);

        foreach (var active in new[] { false, true })
        {
            using var response = await Client.PatchAsync(Url(original.Id, active), null);
            await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
            await AssertSavedAsync(original, active);
            await AssertSavedAsync(control, true);
            (await ReadDoctorsAsync()).ShouldBe(doctors);
            (await ReadRelationsAsync()).ShouldBe(relations);
            await AssertDirectoryAsync(original.Id, control.Id, active);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Administrator)]
    public async Task Ciclo_HTTP_actualiza_catalogo_busqueda_totales_y_filtros_de_cada_rol(UserRole? readerRole)
    {
        var target = await SeedAsync();
        var control = await SeedAsync(name: $"Neurología {Guid.NewGuid():N}");
        await AuthorizeAsync(UserRole.Administrator, Client);
        using var reader = Factory.CreateClient();
        if (readerRole.HasValue) await AuthorizeAsync(readerRole.Value, reader);

        foreach (var phase in new[] { 0, 1, 2 })
        {
            var active = phase != 1;
            if (phase > 0)
            {
                using var response = await Client.PatchAsync(Url(target.Id, active), null);
                await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
            }
            await AssertSavedAsync(target, active);
            foreach (var status in new string?[] { null, "Active", "Inactive", "All" })
            {
                var administrator = readerRole == UserRole.Administrator;
                var expected = administrator
                    ? status switch
                    {
                        "Inactive" => active ? [] : new[] { target },
                        "All" => new[] { target, control },
                        _ => active ? new[] { target, control } : new[] { control }
                    }
                    : active ? new[] { target, control } : new[] { control };
                await AssertCatalogAsync(reader, null, status, expected, administrator);
                await AssertCatalogAsync(reader, target.Name.ToUpperInvariant(), status,
                    expected.Where(value => value.Id == target.Id).ToArray(), administrator);
            }
        }
    }

    private async Task AssertCatalogAsync(HttpClient client, string? search, string? status, Specialty[] expected, bool includeState)
    {
        var query = new Dictionary<string, string?>();
        if (search is not null) query["search"] = search;
        if (status is not null) query["status"] = status;
        using var response = await client.GetAsync(QueryHelpers.AddQueryString("/api/specialties", query));
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCount").GetInt32().ShouldBe(expected.Length);
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        items.Select(item => item.GetProperty("id").GetInt32())
            .ShouldBe(expected.Select(value => value.Id), ignoreOrder: true);
        foreach (var item in items)
        {
            var specialty = expected.Single(value => value.Id == item.GetProperty("id").GetInt32());
            item.GetProperty("name").GetString().ShouldBe(specialty.Name);
            item.GetProperty("description").GetString().ShouldBe(specialty.Description);
            item.TryGetProperty("isActive", out var state).ShouldBe(includeState);
            if (includeState)
            {
                using var scope = Factory.Services.CreateScope();
                var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Specialties.AsNoTracking()
                    .SingleAsync(value => value.Id == specialty.Id);
                state.GetBoolean().ShouldBe(saved.IsActive);
            }
        }
    }

    private async Task AuthorizeAsync(UserRole role, HttpClient client)
    {
        const string password = "Secreta123";
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var email = $"activation-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(role, email, password, mustChangePassword: false);
        var user = (await FindUserAsync(email))!;
        _userIds.Add(user.Id);
        using (var scope = Factory.Services.CreateScope())
            await TestDataSeeder.InsertProfileAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), user);
        using var response = await LoginAsync(email, password, client);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var login = await response.ReadLoginAsync();
        login.RequiresTwoFactor.ShouldBeFalse();
        login.PendingStep.ShouldBeNull();
        login.AccessToken.ShouldNotBeNullOrWhiteSpace();
        var jwt = new JsonWebToken(login.AccessToken);
        jwt.Claims.Where(claim => claim.Type == AppClaims.Role).Select(claim => claim.Value)
            .ShouldBe(new[] { role.ToString() });
        jwt.Claims.ShouldNotContain(claim => claim.Type == AppClaims.PendingStep);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private async Task AddDoctorsAsync(int specialtyId)
    {
        for (var index = 0; index < 2; index++)
        {
            var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"linked-{Guid.NewGuid():N}@correo.com").Value);
            await Factory.SaveAsync(user);
            _userIds.Add(user.Id);
            using var scope = Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            _doctorIds.Add(doctorId);
            (await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId)).ShouldBe(1);
        }
        (await ReadRelationsAsync()).Length.ShouldBe(2);
    }

    private async Task AssertDirectoryAsync(int targetId, int controlId, bool active)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var index = 0; index < _doctorIds.Count; index++)
        {
            var expected = active ? new[] { targetId } : Array.Empty<int>();
            if (index == 0) expected = expected.Append(controlId).ToArray();
            var directory = await TestDataSeeder.ReadDirectorySpecialtiesAsync(context, _doctorIds[index]);
            directory.Ids.ShouldBe(expected, ignoreOrder: true);
            directory.Ids.Length.ShouldBe(directory.Names.Length);
            var expectedSpecialties = await context.Specialties.AsNoTracking()
                .Where(value => expected.Contains(value.Id)).Select(value => new { value.Id, value.Name }).ToListAsync();
            directory.Ids.Zip(directory.Names)
                .ShouldBe(expectedSpecialties.Select(value => (value.Id, value.Name)), ignoreOrder: true);
        }
    }

    private async Task<Specialty> SeedAsync(bool active = true, string? name = null)
    {
        var specialty = Specialty.Create(name ?? $"Cardiología {Guid.NewGuid():N}", "Atención MÉDICA.\nDatos conservados.").Value;
        if (!active) specialty.Deactivate().IsSuccess.ShouldBeTrue();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Specialties.Add(specialty);
        await context.SaveChangesAsync();
        _specialtyIds.Add(specialty.Id);
        specialty.Id.ShouldBeGreaterThan(0);
        return specialty;
    }

    private async Task AssertSavedAsync(Specialty original, bool active)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await context.Specialties.AsNoTracking().SingleAsync(value => value.Id == original.Id);
        saved.Id.ShouldBe(original.Id);
        saved.Name.ShouldBe(original.Name);
        saved.Description.ShouldBe(original.Description);
        saved.IsActive.ShouldBe(active);
        (await context.Specialties.Select(value => value.Id).ToArrayAsync()).ShouldBe(_specialtyIds, ignoreOrder: true);
    }

    private async Task<string[]> SnapshotAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.SqlQuery<string>(
            $"SELECT to_jsonb(s)::text AS \"Value\" FROM people.specialty s ORDER BY s.id").ToListAsync()).ToArray();
    }

    private async Task<string[]> ReadRelationsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await TestDataSeeder.ReadDoctorSpecialtiesAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), _doctorIds.ToArray());
    }

    private async Task<string[]> ReadDoctorsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await TestDataSeeder.ReadDoctorsAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), _doctorIds.ToArray());
    }

    public override async Task DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var scope = Factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doctorIds = _doctorIds.ToArray();
            var userIds = _userIds.ToArray();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM people.doctor_specialty WHERE doctor_id = ANY({doctorIds})", timeout.Token);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM people.doctor WHERE user_id = ANY({userIds})", timeout.Token);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM people.patient WHERE user_id = ANY({userIds})", timeout.Token);
            await context.Specialties.Where(value => _specialtyIds.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
            await context.Users.Where(value => userIds.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
            (await context.Specialties.AnyAsync(value => _specialtyIds.Contains(value.Id), timeout.Token)).ShouldBeFalse();
            (await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, doctorIds)).ShouldBeEmpty();
            (await context.Users.AnyAsync(value => userIds.Contains(value.Id), timeout.Token)).ShouldBeFalse();
        }
        finally
        {
            await base.DisposeAsync();
        }
    }

    private static string Url(int id, bool activate) => $"/api/specialties/{id}/{(activate ? "activate" : "deactivate")}";

    private sealed class SaveObserver : SaveChangesInterceptor
    {
        public int Calls { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TransactionObserver : DbTransactionInterceptor
    {
        public int Starts { get; private set; }
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            Starts++;
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Commits++;
            return Task.CompletedTask;
        }

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Rollbacks++;
            return Task.CompletedTask;
        }
    }
}
