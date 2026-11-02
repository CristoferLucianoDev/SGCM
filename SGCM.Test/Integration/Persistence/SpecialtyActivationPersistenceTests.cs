using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class SpecialtyActivationPersistenceTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private readonly List<int> _specialtyIds = [];
    private readonly List<int> _userIds = [];
    private readonly List<int> _doctorIds = [];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetById_recupera_ambos_estados_con_tracking_aun_si_el_contexto_no_rastrea(bool active)
    {
        var original = await SeedAsync(active);
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var loaded = await scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>()
            .GetByIdAsync(original.Id, CancellationToken.None);

        loaded.ShouldNotBeNull();
        AssertValues(loaded, original, active);
        context.Entry(loaded).State.ShouldBe(EntityState.Unchanged);
        context.ChangeTracker.Entries<Specialty>().ShouldHaveSingleItem().Entity.ShouldBeSameAs(loaded);
    }

    [Fact]
    public async Task Ciclo_completo_persiste_el_mismo_ID_y_los_datos_desde_contextos_independientes()
    {
        var original = await SeedAsync();
        await AssertSavedAsync(original, true);

        await ChangeAsync(original.Id, false);
        await AssertSavedAsync(original, false);

        await ChangeAsync(original.Id, true);
        await AssertSavedAsync(original, true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Conflicto_no_agrega_cambios_rastreados_ni_actualiza_la_fila_o_su_auditoria(bool activate)
    {
        var original = await SeedAsync(activate);
        var before = await SnapshotAsync(original.Id);
        await using (var provider = CreateProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var specialty = (await scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>()
                .GetByIdAsync(original.Id, CancellationToken.None))!;

            var result = activate ? specialty.Activate() : specialty.Deactivate();

            result.Error.ShouldBe(activate
                ? ActivationErrors.AlreadyActive(nameof(Specialty))
                : ActivationErrors.AlreadyInactive(nameof(Specialty)));
            context.ChangeTracker.DetectChanges();
            context.Entry(specialty).State.ShouldBe(EntityState.Unchanged);
            context.Entry(specialty).Properties.ShouldAllBe(property => !property.IsModified);
            (await context.SaveChangesAsync()).ShouldBe(0);
        }
        (await SnapshotAsync(original.Id)).ShouldBe(before);
        await AssertSavedAsync(original, activate);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rollback_revierte_el_cambio_de_estado_ya_enviado_a_PostgreSQL(bool activate)
    {
        var original = await SeedAsync(!activate);
        var before = await SnapshotAsync(original.Id);
        await using (var provider = CreateProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);
            var specialty = (await scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>()
                .GetByIdAsync(original.Id, CancellationToken.None))!;
            (activate ? specialty.Activate() : specialty.Deactivate()).IsSuccess.ShouldBeTrue();
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await context.Specialties.AsNoTracking().SingleAsync(value => value.Id == original.Id))
                .IsActive.ShouldBe(activate);
            await AssertSavedAsync(original, !activate);

            await transaction.RollbackAsync(CancellationToken.None);
        }
        (await SnapshotAsync(original.Id)).ShouldBe(before);
        await AssertSavedAsync(original, !activate);
    }

    [Fact]
    public async Task Ciclo_conserva_dos_medicos_y_todas_las_relaciones_y_filtra_solo_las_especialidades_del_directorio()
    {
        var original = await SeedAsync();
        var control = await SeedAsync();
        await using (var context = CreateContext())
        {
            for (var index = 0; index < 2; index++)
            {
                var user = UserMother.WithRole(UserRole.Doctor,
                    email: Email.Create($"doctor-{Guid.NewGuid():N}@correo.com").Value);
                user.ClearDomainEvents();
                context.Users.Add(user);
                await context.SaveChangesAsync();
                _userIds.Add(user.Id);
                var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
                _doctorIds.Add(doctorId);
                (await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, original.Id)).ShouldBe(1);
            }
            (await TestDataSeeder.InsertDoctorSpecialtyAsync(context, _doctorIds[0], control.Id)).ShouldBe(1);
        }

        await using var verification = CreateContext();
        var relations = await TestDataSeeder.ReadDoctorSpecialtiesAsync(verification, _doctorIds.ToArray());
        relations.Length.ShouldBe(3);
        relations.Count(row =>
        {
            using var document = JsonDocument.Parse(row);
            return document.RootElement.GetProperty("specialty_id").GetInt32() == original.Id;
        }).ShouldBe(2);
        var doctors = await TestDataSeeder.ReadDoctorsAsync(verification, _doctorIds.ToArray());
        doctors.Length.ShouldBe(2);

        await AssertDirectoryAsync(original.Id, control.Id, true);
        await ChangeAsync(original.Id, false);
        await VerifyGraphAsync(original, control.Id, false, doctors, relations);
        await ChangeAsync(original.Id, true);
        await VerifyGraphAsync(original, control.Id, true, doctors, relations);
    }

    [Fact]
    public async Task Reset_limpia_asociaciones_perfiles_y_especialidades_sin_modificar_los_datos_geograficos_semilla()
    {
        var specialty = await SeedAsync();
        await using (var context = CreateContext())
        {
            var user = UserMother.WithRole(UserRole.Doctor,
                email: Email.Create($"reset-{Guid.NewGuid():N}@correo.com").Value);
            user.ClearDomainEvents();
            context.Users.Add(user);
            await context.SaveChangesAsync();
            _userIds.Add(user.Id);
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            _doctorIds.Add(doctorId);
            (await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialty.Id)).ShouldBe(1);
            (await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, _doctorIds.ToArray())).ShouldHaveSingleItem();
        }
        var geography = await ReadGeographyAsync();
        geography.ShouldNotBeEmpty();

        await Database.ResetAsync();

        await using var verification = CreateContext();
        (await verification.Specialties.AnyAsync()).ShouldBeFalse();
        (await verification.Users.AnyAsync(value => _userIds.Contains(value.Id))).ShouldBeFalse();
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(verification, _doctorIds.ToArray())).ShouldBeEmpty();
        (await TestDataSeeder.ReadDoctorsAsync(verification, _doctorIds.ToArray())).ShouldBeEmpty();
        (await ReadGeographyAsync()).ShouldBe(geography);
    }

    private async Task<string[]> ReadGeographyAsync()
    {
        await using var context = CreateContext();
        return (await context.Database.SqlQuery<string>(
            $"""
            SELECT 'province:' || to_jsonb(p)::text AS "Value" FROM people.province p
            UNION ALL
            SELECT 'municipality:' || to_jsonb(m)::text AS "Value" FROM people.municipality m
            ORDER BY "Value"
            """).ToListAsync()).ToArray();
    }

    private async Task VerifyGraphAsync(Specialty original, int controlId, bool active, string[] doctors, string[] relations)
    {
        await AssertSavedAsync(original, active);
        await using var context = CreateContext();
        (await TestDataSeeder.ReadDoctorsAsync(context, _doctorIds.ToArray())).ShouldBe(doctors);
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, _doctorIds.ToArray())).ShouldBe(relations);
        await AssertDirectoryAsync(original.Id, controlId, active);
    }

    private async Task AssertDirectoryAsync(int specialtyId, int controlId, bool active)
    {
        await using var context = CreateContext();
        for (var index = 0; index < _doctorIds.Count; index++)
        {
            var expected = active ? new[] { specialtyId } : Array.Empty<int>();
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

    private async Task ChangeAsync(int id, bool activate)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var specialty = (await scope.ServiceProvider.GetRequiredService<ISpecialtyRepository>()
            .GetByIdAsync(id, CancellationToken.None))!;
        (activate ? specialty.Activate() : specialty.Deactivate()).IsSuccess.ShouldBeTrue();
        context.ChangeTracker.DetectChanges();
        context.Entry(specialty).Properties.Where(property => property.IsModified)
            .Select(property => property.Metadata.Name).ShouldBe(new[] { nameof(Specialty.IsActive) });
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    private async Task<Specialty> SeedAsync(bool active = true)
    {
        var specialty = Specialty.Create($"Cardiología {Guid.NewGuid():N}", "Atención MÉDICA.\nConserva los datos.").Value;
        if (!active) specialty.Deactivate().IsSuccess.ShouldBeTrue();
        await using var context = CreateContext();
        context.Specialties.Add(specialty);
        await context.SaveChangesAsync();
        specialty.Id.ShouldBeGreaterThan(0);
        _specialtyIds.Add(specialty.Id);
        return specialty;
    }

    private async Task AssertSavedAsync(Specialty original, bool active)
    {
        await using var context = CreateContext();
        AssertValues(await context.Specialties.AsNoTracking().SingleAsync(value => value.Id == original.Id), original, active);
    }

    private static void AssertValues(Specialty saved, Specialty original, bool active)
    {
        saved.Id.ShouldBe(original.Id);
        saved.Name.ShouldBe(original.Name);
        saved.Description.ShouldBe(original.Description);
        saved.IsActive.ShouldBe(active);
    }

    private async Task<string> SnapshotAsync(int id)
    {
        await using var context = CreateContext();
        return await context.Database.SqlQuery<string>(
            $"SELECT to_jsonb(s)::text AS \"Value\" FROM people.specialty s WHERE s.id = {id}").SingleAsync();
    }

    public override async Task DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var context = CreateContext();
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
        }
        finally
        {
            await base.DisposeAsync();
        }
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(Database.ConnectionString).UseSnakeCaseNamingConvention().Options);

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options => options.SgcmDatabase = Database.ConnectionString);
        services.AddInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
