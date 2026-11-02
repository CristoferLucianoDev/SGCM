using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.People;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class DoctorRepositoryTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetByUserId_carga_todo_con_tracking_incluyendo_vinculos_inactivos(bool active)
    {
        await using var seedContext = Context();
        var seed = await DoctorPersistenceData.SeedAsync(seedContext, active: active);
        seed.UserId.ShouldNotBe(seed.DoctorId);
        await using var context = Context();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        await using var transaction = await context.Database.BeginTransactionAsync();

        var doctor = await new DoctorRepository(context).GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None);

        doctor.ShouldNotBeNull();
        doctor.Id.ShouldBe(seed.DoctorId);
        doctor.UserId.ShouldBe(seed.UserId);
        doctor.IsActive.ShouldBe(active);
        doctor.FirstName.ShouldBe("Luis");
        doctor.LastName.ShouldBe("Pérez");
        doctor.LicenseNumber.ShouldStartWith("TASK226-");
        doctor.PhoneNumber.ShouldBe("8095550200");
        doctor.ProvinceId.ShouldBeGreaterThan(0);
        doctor.MunicipalityId.ShouldBeGreaterThan(0);
        doctor.AddressLine.ShouldBe("Calle de pruebas 226");
        doctor.ConsultationFee.ShouldBe(1500m);
        doctor.YearsOfExperience.ShouldBe(10);
        doctor.Biography.ShouldBe("Perfil médico de prueba.");
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(seed.SpecialtyIds.Take(2), ignoreOrder: true);
        context.Entry(doctor).State.ShouldBe(EntityState.Unchanged);
        context.ChangeTracker.Entries<DoctorSpecialty>().Count().ShouldBe(2);
        context.ChangeTracker.Entries<DoctorSpecialty>().ShouldAllBe(entry => entry.State == EntityState.Unchanged);
        doctor.Specialties.ShouldBeAssignableTo<ICollection<DoctorSpecialty>>().IsReadOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task GetByUserId_no_encuentra_otro_perfil_y_materializa_legados_sin_inventar_vinculos()
    {
        await using var context = Context();
        var seed = await DoctorPersistenceData.SeedAsync(context, associations: 0);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var repository = new DoctorRepository(context);

        (await repository.GetByUserIdForUpdateAsync(int.MaxValue, CancellationToken.None)).ShouldBeNull();
        var legacy = await repository.GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None);

        legacy.ShouldNotBeNull();
        legacy.Specialties.ShouldBeEmpty();
        context.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task GetByUserId_exige_transaccion_y_propaga_cancelacion()
    {
        await using var context = Context();
        var repository = new DoctorRepository(context);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            repository.GetByUserIdForUpdateAsync(1, CancellationToken.None));
        await using var transaction = await context.Database.BeginTransactionAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Should.ThrowAsync<OperationCanceledException>(() =>
            repository.GetByUserIdForUpdateAsync(1, cancellation.Token));

        error.CancellationToken.ShouldBe(cancellation.Token);
        context.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task GetByUserId_recarga_vinculos_obsoletos_sin_descartar_cambios_de_otros_agregados()
    {
        await using var seedContext = Context();
        var seed = await DoctorPersistenceData.SeedAsync(seedContext);
        await using var context = Context();
        var stale = await context.Doctors.Include(doctor => doctor.Specialties).SingleAsync();
        await using (var other = Context())
            await other.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM people.doctor_specialty WHERE doctor_id = {seed.DoctorId} AND specialty_id = {seed.SpecialtyIds[0]}");
        var unrelated = Specialty.Create("Especialidad pendiente", "No debe descartarse.").Value;
        context.Specialties.Add(unrelated);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var fresh = await new DoctorRepository(context).GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None);

        fresh.ShouldNotBeNull();
        fresh.ShouldNotBeSameAs(stale);
        fresh.Specialties.ShouldHaveSingleItem().SpecialtyId.ShouldBe(seed.SpecialtyIds[1]);
        context.Entry(unrelated).State.ShouldBe(EntityState.Added);
        context.ChangeTracker.Entries<DoctorSpecialty>().ShouldHaveSingleItem().State.ShouldBe(EntityState.Unchanged);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("associate")]
    [InlineData("disassociate")]
    public async Task GetByUserId_no_descarta_cambios_pendientes_del_grafo(string operation)
    {
        await using var context = Context();
        var seed = await DoctorPersistenceData.SeedAsync(context);
        var doctor = await context.Doctors.SingleAsync();
        if (operation == "profile") doctor.Deactivate().IsSuccess.ShouldBeTrue();
        else if (operation == "associate") doctor.AssociateSpecialty(seed.SpecialtyIds[2]).IsSuccess.ShouldBeTrue();
        else doctor.DisassociateSpecialty(seed.SpecialtyIds[0]).IsSuccess.ShouldBeTrue();
        var links = doctor.Specialties.ToArray();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            new DoctorRepository(context).GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None));

        context.ChangeTracker.HasChanges().ShouldBeTrue();
        context.Entry(doctor).State.ShouldBe(operation == "profile" ? EntityState.Modified : EntityState.Unchanged);
        var expected = operation == "profile" ? seed.SpecialtyIds.Take(2)
            : operation == "associate" ? seed.SpecialtyIds : [seed.SpecialtyIds[1]];
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(expected, ignoreOrder: true);
        doctor.Specialties.ShouldBe(links);
        if (operation == "disassociate")
            context.ChangeTracker.Entries<DoctorSpecialty>().Single(entry => entry.State == EntityState.Deleted)
                .Entity.SpecialtyId.ShouldBe(seed.SpecialtyIds[0]);
    }

    [Fact]
    public async Task Add_persiste_identidad_y_vinculos_inmutables_desde_la_fabrica()
    {
        await using var context = Context();
        var seed = await DoctorPersistenceData.SeedAsync(context);
        var source = await context.Doctors.AsNoTracking().SingleAsync();
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"new-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var doctor = DoctorMother.Create([seed.SpecialtyIds[0]], userId: user.Id,
            provinceId: source.ProvinceId, municipalityId: source.MunicipalityId,
            licenseNumber: $"LIC-{Guid.NewGuid():N}").Value;
        doctor.Deactivate().IsSuccess.ShouldBeTrue();
        var repository = new DoctorRepository(context);

        repository.Add(doctor);
        doctor.Id.ShouldBe(0);
        context.Entry(doctor).State.ShouldBe(EntityState.Added);
        await context.SaveChangesAsync();
        doctor.Id.ShouldBeGreaterThan(0);
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var loaded = await repository.GetByUserIdForUpdateAsync(user.Id, CancellationToken.None);

        loaded.ShouldNotBeNull();
        loaded.Id.ShouldBe(doctor.Id);
        loaded.UserId.ShouldBe(user.Id);
        loaded.IsActive.ShouldBeFalse();
        loaded.FirstName.ShouldBe(DoctorMother.FirstName);
        loaded.ConsultationFee.ShouldBe(DoctorMother.ConsultationFee);
        loaded.Specialties.ShouldHaveSingleItem().SpecialtyId.ShouldBe(seed.SpecialtyIds[0]);
        (await context.Doctors.AsNoTracking().SingleAsync(value => value.Id == seed.DoctorId))
            .Specialties.Select(link => link.SpecialtyId).ShouldBe(seed.SpecialtyIds.Take(2), ignoreOrder: true);
    }

    private AppDbContext Context() => DoctorPersistenceData.Context(Database.ConnectionString);
}

internal sealed record DoctorSeed(int UserId, int DoctorId, int[] SpecialtyIds);

internal static class DoctorPersistenceData
{
    public static AppDbContext Context(string connectionString) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connectionString).UseSnakeCaseNamingConvention().Options);

    public static ServiceProvider Provider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options => options.SgcmDatabase = connectionString);
        services.AddInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static async Task<DoctorSeed> SeedAsync(AppDbContext context, int associations = 2, bool active = true)
    {
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"doctor-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        var specialties = Enumerable.Range(0, 3)
            .Select(index => Specialty.Create($"Especialidad {Guid.NewGuid():N}", "Descripción del catálogo.").Value).ToArray();
        specialties[1].Deactivate().IsSuccess.ShouldBeTrue();
        context.Specialties.AddRange(specialties);
        await context.SaveChangesAsync();
        var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
        // IDs distintos para detectar una resolución errónea por Doctor.Id en lugar de UserId.
        var userId = user.Id;
        if (userId == doctorId)
        {
            var replacement = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"owner-{Guid.NewGuid():N}@correo.com").Value);
            context.Users.Add(replacement);
            await context.SaveChangesAsync();
            userId = replacement.Id;
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE people.doctor SET user_id = {userId} WHERE id = {doctorId}");
        }
        foreach (var specialty in specialties.Take(associations))
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialty.Id);
        if (!active)
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE people.doctor SET is_active = false WHERE id = {doctorId}");
        return new DoctorSeed(userId, doctorId, specialties.Select(value => value.Id).ToArray());
    }
}
