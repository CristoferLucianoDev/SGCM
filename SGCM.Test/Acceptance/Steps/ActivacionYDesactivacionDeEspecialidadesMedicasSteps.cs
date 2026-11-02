using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class ActivacionYDesactivacionDeEspecialidadesMedicasSteps(ApiScenario scenario)
{
    private readonly List<int> _specialtyIds = [];
    private readonly List<int> _userIds = [];
    private readonly List<int> _doctorIds = [];
    private Specialty _target = null!;
    private Specialty _control = null!;
    private string[] _specialtiesBefore = [];
    private string[] _relationsBefore = [];
    private string[] _doctorsBefore = [];
    private string[] _originalRelations = [];
    private string[] _originalDoctors = [];

    [Given("que preparo una especialidad {string} para activar o desactivar")]
    public async Task PrepararEspecialidad(string state) =>
        _target = await SeedAsync($"Cardiología {Guid.NewGuid():N}", IsActive(state));

    [Given("que preparo una especialidad de control para las consultas")]
    public async Task PrepararControl() =>
        _control = await SeedAsync($"Neurología {Guid.NewGuid():N}", true);

    [Given("que dos médicos tienen esa especialidad y una asociación de control")]
    public async Task PrepararRelaciones()
    {
        await PrepararControl();
        for (var index = 0; index < 2; index++)
        {
            var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"bdd-linked-{Guid.NewGuid():N}@correo.com").Value);
            await scenario.Factory.SaveAsync(user);
            _userIds.Add(user.Id);
            using var scope = scenario.Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            _doctorIds.Add(doctorId);
            (await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, _target.Id)).ShouldBe(1);
        }
        using (var scope = scenario.Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await TestDataSeeder.InsertDoctorSpecialtyAsync(context, _doctorIds[0], _control.Id)).ShouldBe(1);
            _originalRelations = await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, _doctorIds.ToArray());
            _originalDoctors = await TestDataSeeder.ReadDoctorsAsync(context, _doctorIds.ToArray());
        }
        _originalDoctors.Length.ShouldBe(2);
        _originalRelations.Length.ShouldBe(3);
        _originalRelations.Count(row =>
        {
            using var document = JsonDocument.Parse(row);
            return document.RootElement.GetProperty("specialty_id").GetInt32() == _target.Id;
        }).ShouldBe(2);
    }

    [When("solicito {string} para la especialidad preparada")]
    public async Task SolicitarCambio(string operation)
    {
        var action = operation switch
        {
            "desactivar" => "deactivate",
            "reactivar" => "activate",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        using (var scope = scenario.Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            _specialtiesBefore = await SnapshotAsync(context);
            _relationsBefore = await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, _doctorIds.ToArray());
            _doctorsBefore = await TestDataSeeder.ReadDoctorsAsync(context, _doctorIds.ToArray());
        }
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.PatchAsync($"/api/specialties/{_target.Id}/{action}", null);
    }

    [Then("la especialidad preparada queda {string} en PostgreSQL conservando sus datos")]
    public async Task VerificarEstado(string state)
    {
        (await scenario.Response!.Content.ReadAsStringAsync()).ShouldBeEmpty();
        using var scope = scenario.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await context.Specialties.AsNoTracking().SingleAsync(value => value.Id == _target.Id);
        saved.Id.ShouldBe(_target.Id);
        saved.Name.ShouldBe(_target.Name);
        saved.Description.ShouldBe(_target.Description);
        saved.IsActive.ShouldBe(IsActive(state));
        (await context.Specialties.Select(value => value.Id).ToArrayAsync()).ShouldBe(_specialtyIds, ignoreOrder: true);
        if (_control is not null)
        {
            var control = await context.Specialties.AsNoTracking().SingleAsync(value => value.Id == _control.Id);
            control.Name.ShouldBe(_control.Name);
            control.Description.ShouldBe(_control.Description);
            control.IsActive.ShouldBeTrue();
        }
    }

    [Then("el error de cambio de estado tiene código {string}")]
    public async Task VerificarError(string code) =>
        (await scenario.Response!.ReadErrorCodeAsync()).ShouldBe(code);

    [Then("el intento de cambio de estado no altera datos ni relaciones")]
    public async Task VerificarSinCambios()
    {
        using var scope = scenario.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await SnapshotAsync(context)).ShouldBe(_specialtiesBefore);
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, _doctorIds.ToArray())).ShouldBe(_relationsBefore);
        (await TestDataSeeder.ReadDoctorsAsync(context, _doctorIds.ToArray())).ShouldBe(_doctorsBefore);
    }

    [Then("los dos médicos y sus asociaciones permanecen intactos")]
    public async Task VerificarRelaciones()
    {
        _originalDoctors.Length.ShouldBe(2);
        _originalRelations.Length.ShouldBe(3);
        using var scope = scenario.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await TestDataSeeder.ReadDoctorsAsync(context, _doctorIds.ToArray())).ShouldBe(_originalDoctors);
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, _doctorIds.ToArray())).ShouldBe(_originalRelations);
    }

    [Then("el directorio médico muestra la especialidad preparada {string} y conserva la de control")]
    public async Task VerificarDirectorio(string state)
    {
        _doctorIds.Count.ShouldBe(2);
        using var scope = scenario.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var index = 0; index < _doctorIds.Count; index++)
        {
            var expected = IsActive(state) ? new[] { _target.Id } : Array.Empty<int>();
            if (index == 0) expected = expected.Append(_control.Id).ToArray();
            var directory = await TestDataSeeder.ReadDirectorySpecialtiesAsync(context, _doctorIds[index]);
            directory.Ids.ShouldBe(expected, ignoreOrder: true);
            directory.Ids.Length.ShouldBe(directory.Names.Length);
            var expectedSpecialties = await context.Specialties.AsNoTracking()
                .Where(value => expected.Contains(value.Id)).Select(value => new { value.Id, value.Name }).ToListAsync();
            directory.Ids.Zip(directory.Names)
                .ShouldBe(expectedSpecialties.Select(value => (value.Id, value.Name)), ignoreOrder: true);
        }
    }

    [Then("el catálogo y la búsqueda reflejan la especialidad preparada {string} según el consumidor")]
    public async Task VerificarConsultas(string state)
    {
        var active = IsActive(state);
        using var publicClient = scenario.Factory.CreateClient();
        foreach (var status in new string?[] { null, "Active", "Inactive", "All" })
        {
            var publicExpected = active ? new[] { _target, _control } : new[] { _control };
            await AssertCatalogAsync(publicClient, null, status, publicExpected, false, active);
            await AssertCatalogAsync(publicClient, _target.Name.ToUpperInvariant(), status,
                active ? [_target] : [], false, active);
            var adminExpected = status switch
            {
                "Inactive" => active ? [] : new[] { _target },
                "All" => new[] { _target, _control },
                _ => publicExpected
            };
            await AssertCatalogAsync(scenario.Client, null, status, adminExpected, true, active);
            await AssertCatalogAsync(scenario.Client, _target.Name.ToUpperInvariant(), status,
                adminExpected.Where(value => value.Id == _target.Id).ToArray(), true, active);
        }
    }

    private async Task AssertCatalogAsync(HttpClient client, string? search, string? status,
        Specialty[] expected, bool includeState, bool targetActive)
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
            var original = expected.Single(value => value.Id == item.GetProperty("id").GetInt32());
            item.GetProperty("name").GetString().ShouldBe(original.Name);
            item.GetProperty("description").GetString().ShouldBe(original.Description);
            item.TryGetProperty("isActive", out var itemState).ShouldBe(includeState);
            if (includeState) itemState.GetBoolean().ShouldBe(original.Id != _target.Id || targetActive);
        }
    }

    [AfterScenario("SpecialtyActivation")]
    public async Task LimpiarDatosAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctorIds = _doctorIds.ToArray();
        var userIds = _userIds.ToArray();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM people.doctor_specialty WHERE doctor_id = ANY({doctorIds})", timeout.Token);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM people.doctor WHERE id = ANY({doctorIds})", timeout.Token);
        await context.Specialties.Where(value => _specialtyIds.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
        await context.Users.Where(value => userIds.Contains(value.Id)).ExecuteDeleteAsync(timeout.Token);
        (await context.Specialties.AnyAsync(value => _specialtyIds.Contains(value.Id), timeout.Token)).ShouldBeFalse();
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, doctorIds)).ShouldBeEmpty();
        (await TestDataSeeder.ReadDoctorsAsync(context, doctorIds)).ShouldBeEmpty();
        (await context.Users.AnyAsync(value => userIds.Contains(value.Id), timeout.Token)).ShouldBeFalse();
    }

    private async Task<Specialty> SeedAsync(string name, bool active)
    {
        var specialty = Specialty.Create(name, "Atención MÉDICA.\nConserva la identidad.").Value;
        if (!active) specialty.Deactivate().IsSuccess.ShouldBeTrue();
        using var scope = scenario.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Specialties.Add(specialty);
        await context.SaveChangesAsync();
        specialty.Id.ShouldBeGreaterThan(0);
        _specialtyIds.Add(specialty.Id);
        return specialty;
    }

    private static async Task<string[]> SnapshotAsync(AppDbContext context) =>
        (await context.Database.SqlQuery<string>(
            $"SELECT to_jsonb(s)::text AS \"Value\" FROM people.specialty s ORDER BY s.id").ToListAsync()).ToArray();

    private static bool IsActive(string state) => state switch
    {
        "activa" => true,
        "inactiva" => false,
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
